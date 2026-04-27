using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.AiSafety;

/// <summary>
/// Scoped implementation of <see cref="IHallucinationTrackingService"/> (US_074 task_002, AC-1, AC-2, AIR-Q06).
///
/// <para>
/// Writes verification records and alerts using <see cref="ApplicationDbContext"/>.
/// All log statements reference <c>MedicalCodeId</c> only — no patient PII is written
/// to logs (AIR-Q06 audit compliance).
/// </para>
/// </summary>
public sealed class HallucinationTrackingService : IHallucinationTrackingService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Hallucination rate threshold that triggers a critical alert (AC-2).</summary>
    private const double AlertThreshold = 0.05;

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext                   _db;
    private readonly ILogger<HallucinationTrackingService> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public HallucinationTrackingService(
        ApplicationDbContext                   db,
        ILogger<HallucinationTrackingService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IHallucinationTrackingService — RecordVerificationAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task RecordVerificationAsync(
        Guid                medicalCodeId,
        Guid                verifierUserId,
        SourceSupportStatus status,
        string?             notes,
        CancellationToken   ct = default)
    {
        var record = new HallucinationRecord
        {
            MedicalCodeId       = medicalCodeId,
            VerifiedByUserId    = verifierUserId,
            SourceSupportStatus = status,
            VerificationNotes   = notes,
            VerifiedAt          = DateTime.UtcNow,
            IsRetroactive       = false,
        };

        _db.HallucinationRecords.Add(record);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "HallucinationTracking: verification recorded. " +
            "MedicalCodeId={MedicalCodeId} Status={Status} VerifierId={VerifierId}.",
            medicalCodeId, status, verifierUserId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IHallucinationTrackingService — RecordRetroactiveHallucinationAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task RecordRetroactiveHallucinationAsync(
        Guid              medicalCodeId,
        Guid              reporterUserId,
        string            reason,
        CancellationToken ct = default)
    {
        // ── 1. Load medical code — must exist and have been AI-suggested ───────
        var code = await _db.MedicalCodes
            .FirstOrDefaultAsync(c => c.Id == medicalCodeId, ct);

        if (code is null)
        {
            _logger.LogWarning(
                "HallucinationTracking: retroactive hallucination reported for unknown MedicalCodeId={MedicalCodeId}.",
                medicalCodeId);
            return;
        }

        // ── 2. Check for an existing Pending verification record ───────────────
        var existingRecord = await _db.HallucinationRecords
            .FirstOrDefaultAsync(r => r.MedicalCodeId == medicalCodeId, ct);

        if (existingRecord is not null)
        {
            // Update the existing record to reflect the retroactive finding.
            existingRecord.SourceSupportStatus = SourceSupportStatus.Unsupported;
            existingRecord.IsRetroactive       = true;
            existingRecord.VerifiedByUserId    = reporterUserId;
            existingRecord.VerificationNotes   = reason;
            existingRecord.VerifiedAt          = DateTime.UtcNow;
            existingRecord.UpdatedAt           = DateTime.UtcNow;
        }
        else
        {
            // Create a new retroactive verification record.
            _db.HallucinationRecords.Add(new HallucinationRecord
            {
                MedicalCodeId       = medicalCodeId,
                VerifiedByUserId    = reporterUserId,
                SourceSupportStatus = SourceSupportStatus.Unsupported,
                VerificationNotes   = reason,
                VerifiedAt          = DateTime.UtcNow,
                IsRetroactive       = true,
            });
        }

        // ── 3. Reset ApprovedByUserId → requires re-approval (edge case) ──────
        if (code.ApprovedByUserId.HasValue)
        {
            code.ApprovedByUserId = null;
            code.UpdatedAt        = DateTime.UtcNow;
        }

        // ── 4. Create retroactive HallucinationAlert ──────────────────────────
        // Rate is not re-calculated here — the daily job will pick up the change.
        // Set CurrentRate to 0 as a sentinel; the recommendation text carries context.
        string recommendation =
            $"Retroactive hallucination detected in previously approved entry. " +
            $"MedicalCodeId={medicalCodeId}. Re-verification required. Reason: {reason}";

        _db.HallucinationAlerts.Add(new HallucinationAlert
        {
            GeneratedAt      = DateTime.UtcNow,
            CurrentRate      = 0,          // sentinel — exact rate computed by daily job
            TargetRate       = AlertThreshold,
            Recommendation   = recommendation.Length > 500
                ? recommendation[..500]
                : recommendation,
            IsRetroactive    = true,
            IsAcknowledged   = false,
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "HallucinationTracking: retroactive hallucination recorded. " +
            "MedicalCodeId={MedicalCodeId} ReporterId={ReporterId} ApprovalReset={ApprovalReset}.",
            medicalCodeId, reporterUserId, code.ApprovedByUserId is null);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IHallucinationTrackingService — CalculateDailyRateAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<double> CalculateDailyRateAsync(DateTime date, CancellationToken ct = default)
    {
        DateTime startUtc = date.Date.ToUniversalTime();
        DateTime endUtc   = startUtc.AddDays(1);

        int totalVerified = await _db.HallucinationRecords
            .CountAsync(r => r.VerifiedAt >= startUtc && r.VerifiedAt < endUtc, ct);

        if (totalVerified == 0) return 0.0;

        int hallucinationCount = await _db.HallucinationRecords
            .CountAsync(r => r.VerifiedAt >= startUtc && r.VerifiedAt < endUtc
                          && r.SourceSupportStatus == SourceSupportStatus.Unsupported, ct);

        return (double)hallucinationCount / totalVerified;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IHallucinationTrackingService — RunDailyAggregationAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task RunDailyAggregationAsync(CancellationToken ct = default)
    {
        // Aggregate the previous calendar day.
        DateTime targetDate = DateTime.UtcNow.Date.AddDays(-1);
        DateTime startUtc   = targetDate;
        DateTime endUtc     = targetDate.AddDays(1);

        // ── Query verification counts for the target day ─────────────────────
        var records = await _db.HallucinationRecords
            .Where(r => r.VerifiedAt >= startUtc && r.VerifiedAt < endUtc)
            .ToListAsync(ct);

        int totalVerified        = records.Count;
        int hallucinationCount   = records.Count(r => r.SourceSupportStatus == SourceSupportStatus.Unsupported);
        int partiallySupportedCount = records.Count(r => r.SourceSupportStatus == SourceSupportStatus.PartiallySupported);
        double rate              = totalVerified > 0
            ? (double)hallucinationCount / totalVerified
            : 0.0;

        // ── Upsert HallucinationMetric ────────────────────────────────────────
        var existing = await _db.HallucinationMetrics
            .FirstOrDefaultAsync(m => m.MetricDate == targetDate, ct);

        if (existing is not null)
        {
            existing.TotalVerified           = totalVerified;
            existing.HallucinationCount      = hallucinationCount;
            existing.PartiallySupportedCount = partiallySupportedCount;
            existing.HallucinationRate       = rate;
            existing.UpdatedAt               = DateTime.UtcNow;
        }
        else
        {
            _db.HallucinationMetrics.Add(new HallucinationMetric
            {
                MetricDate               = targetDate,
                TotalVerified            = totalVerified,
                HallucinationCount       = hallucinationCount,
                PartiallySupportedCount  = partiallySupportedCount,
                HallucinationRate        = rate,
                TargetRate               = AlertThreshold,
            });
        }

        // ── Generate critical alert if rate > 5% (AC-2) ─────────────────────
        if (totalVerified > 0 && rate > AlertThreshold)
        {
            string recommendation =
                $"Model review recommended — hallucination rate {rate:P1} exceeds " +
                $"{AlertThreshold:P0} target. Review recent model changes and prompt templates.";

            _db.HallucinationAlerts.Add(new HallucinationAlert
            {
                GeneratedAt    = DateTime.UtcNow,
                CurrentRate    = rate,
                TargetRate     = AlertThreshold,
                Recommendation = recommendation.Length > 500
                    ? recommendation[..500]
                    : recommendation,
                IsRetroactive  = false,
                IsAcknowledged = false,
            });

            _logger.LogCritical(
                "HallucinationTracking: hallucination rate {Rate:P1} exceeds {Target:P0} target " +
                "for {Date:yyyy-MM-dd}. Alert generated. HallucinationCount={Count} TotalVerified={Total}.",
                rate, AlertThreshold, targetDate, hallucinationCount, totalVerified);
        }
        else
        {
            _logger.LogInformation(
                "HallucinationTracking: daily aggregation completed. " +
                "Date={Date:yyyy-MM-dd} Rate={Rate:P1} Total={Total} Hallucinations={Count}.",
                targetDate, rate, totalVerified, hallucinationCount);
        }

        await _db.SaveChangesAsync(ct);
    }
}
