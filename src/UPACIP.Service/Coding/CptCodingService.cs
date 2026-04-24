using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Coding;

/// <summary>
/// Manages the CPT procedure code review workflow: reads AI-suggested codes pending staff
/// review, processes approval and override actions, writes HIPAA-compliant audit records,
/// and maintains the Redis cache of pending codes (US_048, AC-1, AC-3, NFR-035).
///
/// AI generation pipeline (task_004_ai_cpt_prompt_rag):
///   CPT code generation via the AI gateway is not implemented in this class.  This service
///   covers only the staff-facing review endpoints; the async generation worker is added in
///   the follow-on task after the RAG retrieval layer and CPT prompt templates are ready.
///
/// Override justification (HIPAA §164.312(b)):
///   When staff overrides an AI suggestion the justification text is stored in
///   <c>MedicalCode.Justification</c> because <c>AuditLog</c> has no free-text details field.
///   The <c>AuditLog</c> records who/what/when; the justification lives on the code row itself.
///
/// Cache:
///   The pending-codes cache key for the patient is invalidated on every approval or override
///   so the frontend receives fresh data on the next GET (NFR-030, 5-minute TTL).
/// </summary>
public sealed class CptCodingService : ICptCodingService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly TimeSpan PendingCacheTtl = TimeSpan.FromMinutes(5); // NFR-030

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext        _db;
    private readonly ICacheService               _cache;
    private readonly ILogger<CptCodingService>   _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public CptCodingService(
        ApplicationDbContext      db,
        ICacheService             cache,
        ILogger<CptCodingService> logger)
    {
        _db     = db;
        _cache  = cache;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICptCodingService — GetPendingCodesAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<IReadOnlyList<MedicalCode>> GetPendingCodesAsync(
        Guid              patientId,
        CancellationToken ct = default)
    {
        return _cache.GetOrSetAsync<IReadOnlyList<MedicalCode>>(
            PendingCacheKey(patientId),
            factory: async () =>
            {
                var rows = await _db.Set<MedicalCode>()
                    .Where(m => m.PatientId         == patientId
                             && m.CodeType          == CodeType.Cpt
                             && m.ApprovedByUserId  == null)
                    .OrderBy(m => m.RelevanceRank ?? int.MaxValue)
                    .ThenByDescending(m => m.CreatedAt)
                    .AsNoTracking()
                    .ToListAsync(ct);

                return (IReadOnlyList<MedicalCode>)rows;
            },
            expiration: PendingCacheTtl,
            cancellationToken: ct)!;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICptCodingService — ApproveCptCodeAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<MedicalCode> ApproveCptCodeAsync(
        Guid              medicalCodeId,
        Guid              approvedByUserId,
        string            correlationId,
        CancellationToken ct = default)
    {
        // ── Load the target row ──────────────────────────────────────────────
        var code = await _db.Set<MedicalCode>()
            .FirstOrDefaultAsync(
                m => m.Id == medicalCodeId && m.CodeType == CodeType.Cpt,
                ct);

        if (code is null)
        {
            _logger.LogWarning(
                "CptCodingService: ApproveCptCodeAsync — MedicalCode not found or not CPT-type. " +
                "MedicalCodeId={MedicalCodeId} UserId={UserId} CorrelationId={CorrelationId}",
                medicalCodeId, approvedByUserId, correlationId);

            throw new KeyNotFoundException(
                $"CPT MedicalCode '{medicalCodeId}' not found.");
        }

        var patientId = code.PatientId;

        // ── Approve ──────────────────────────────────────────────────────────
        code.ApprovedByUserId = approvedByUserId;
        code.UpdatedAt        = DateTime.UtcNow;

        // ── Audit log (HIPAA §164.312(b), NFR-035) ───────────────────────────
        await _db.Set<AuditLog>().AddAsync(new AuditLog
        {
            UserId       = approvedByUserId,
            Action       = AuditAction.CptCodeApproved,
            ResourceType = "MedicalCode",
            ResourceId   = medicalCodeId,
        }, ct);

        await _db.SaveChangesAsync(ct);

        // ── Invalidate cache ─────────────────────────────────────────────────
        await _cache.RemoveAsync(PendingCacheKey(patientId), ct);

        _logger.LogInformation(
            "CptCodingService: CPT code approved. " +
            "MedicalCodeId={MedicalCodeId} CodeValue={CodeValue} UserId={UserId} CorrelationId={CorrelationId}",
            medicalCodeId, code.CodeValue, approvedByUserId, correlationId);

        return code;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICptCodingService — OverrideCptCodeAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<MedicalCode> OverrideCptCodeAsync(
        Guid              medicalCodeId,
        string            replacementCode,
        string            justification,
        Guid              overriddenByUserId,
        string            correlationId,
        CancellationToken ct = default)
    {
        // ── Load the target row ──────────────────────────────────────────────
        var code = await _db.Set<MedicalCode>()
            .FirstOrDefaultAsync(
                m => m.Id == medicalCodeId && m.CodeType == CodeType.Cpt,
                ct);

        if (code is null)
        {
            _logger.LogWarning(
                "CptCodingService: OverrideCptCodeAsync — MedicalCode not found or not CPT-type. " +
                "MedicalCodeId={MedicalCodeId} UserId={UserId} CorrelationId={CorrelationId}",
                medicalCodeId, overriddenByUserId, correlationId);

            throw new KeyNotFoundException(
                $"CPT MedicalCode '{medicalCodeId}' not found.");
        }

        var patientId        = code.PatientId;
        var originalCodeValue = code.CodeValue;

        // ── Apply override ───────────────────────────────────────────────────
        // Justification stored on the code row — AuditLog has no free-text details field.
        code.CodeValue          = replacementCode;
        code.Justification      = justification;
        code.ApprovedByUserId   = overriddenByUserId; // override counts as approved by the overriding user
        code.UpdatedAt          = DateTime.UtcNow;

        // ── Audit log (HIPAA §164.312(b), NFR-035) ───────────────────────────
        await _db.Set<AuditLog>().AddAsync(new AuditLog
        {
            UserId       = overriddenByUserId,
            Action       = AuditAction.CptCodeOverridden,
            ResourceType = "MedicalCode",
            ResourceId   = medicalCodeId,
        }, ct);

        await _db.SaveChangesAsync(ct);

        // ── Invalidate cache ─────────────────────────────────────────────────
        await _cache.RemoveAsync(PendingCacheKey(patientId), ct);

        _logger.LogInformation(
            "CptCodingService: CPT code overridden. " +
            "MedicalCodeId={MedicalCodeId} OriginalCode={OriginalCode} ReplacementCode={ReplacementCode} " +
            "UserId={UserId} CorrelationId={CorrelationId}",
            medicalCodeId, originalCodeValue, replacementCode, overriddenByUserId, correlationId);

        return code;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string PendingCacheKey(Guid patientId)
        => $"upacip:cpt:pending:{patientId}";
}
