using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Verification.Dtos;

namespace UPACIP.Service.Verification;

/// <summary>
/// Scoped implementation of <see cref="IVerificationEnforcementService"/> (US_075, AC-1–AC-4, AIR-S02, AIR-S03).
///
/// <para>
/// Handles verification lifecycle for both <c>MedicalCode</c> and <c>ExtractedData</c> records.
/// Uses <c>CodingAuditLog</c> for medical code operations (preserving old/new code values and
/// justifications) and <c>AuditLog</c> for extracted data operations.
/// </para>
///
/// <para>
/// <b>By design, no auto-approval mechanism exists.</b>  Records remain in pending status
/// indefinitely until a staff member takes action — per AIR-S03 compliance.
/// </para>
///
/// <para>All log statements reference only <c>RecordId</c> and <c>RecordType</c> — no patient PII
/// in log output (AIR-S02, AIR-S03 audit compliance).</para>
/// </summary>
public sealed class VerificationEnforcementService : IVerificationEnforcementService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants — record type discriminators
    // ─────────────────────────────────────────────────────────────────────────

    public const string RecordTypeMedicalCode   = "MedicalCode";
    public const string RecordTypeExtractedData = "ExtractedData";

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext                    _db;
    private readonly ILogger<VerificationEnforcementService> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public VerificationEnforcementService(
        ApplicationDbContext                    db,
        ILogger<VerificationEnforcementService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ApproveAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<VerificationAuditEntryDto?> ApproveAsync(
        Guid              recordId,
        string            recordType,
        Guid              staffUserId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        if (recordType == RecordTypeMedicalCode)
        {
            var code = await _db.MedicalCodes
                .FirstOrDefaultAsync(c => c.Id == recordId, ct);

            if (code is null) return null;

            string originalValue = code.CodeValue;

            code.VerificationStatus = CodeVerificationStatus.Verified;
            code.VerifiedAt         = now;
            code.VerifiedByUserId   = staffUserId;
            code.UpdatedAt          = now;

            _db.CodingAuditLogs.Add(new CodingAuditLog
            {
                MedicalCodeId = recordId,
                PatientId     = code.PatientId,
                Action        = CodingAuditAction.Approved,
                OldCodeValue  = originalValue,
                NewCodeValue  = originalValue,
                UserId        = staffUserId,
                Timestamp     = DateTimeOffset.UtcNow,
                CreatedAt     = now,
            });

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "VerificationEnforcement: MedicalCode approved. RecordId={RecordId} StaffId={StaffId}.",
                recordId, staffUserId);

            return BuildAuditDto(recordId, RecordTypeMedicalCode, staffUserId, now,
                "Approved", originalValue, originalValue, null);
        }

        if (recordType == RecordTypeExtractedData)
        {
            var row = await _db.ExtractedData
                .FirstOrDefaultAsync(r => r.Id == recordId, ct);

            if (row is null) return null;

            string originalValue = DescribeExtractedData(row);

            row.VerificationStatus = VerificationStatus.Verified;
            row.VerifiedByUserId   = staffUserId;
            row.VerifiedAtUtc      = now;
            row.FlaggedForReview   = false;
            row.UpdatedAt          = now;

            _db.AuditLogs.Add(BuildAuditLog(staffUserId, AuditAction.ExtractedDataVerified,
                RecordTypeExtractedData, recordId));

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "VerificationEnforcement: ExtractedData approved. RecordId={RecordId} StaffId={StaffId}.",
                recordId, staffUserId);

            return BuildAuditDto(recordId, RecordTypeExtractedData, staffUserId, now,
                "Approved", originalValue, originalValue, null);
        }

        _logger.LogWarning(
            "VerificationEnforcement: unknown RecordType '{RecordType}' for RecordId={RecordId}.",
            recordType, recordId);
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ModifyAndApproveAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<VerificationAuditEntryDto?> ModifyAndApproveAsync(
        Guid              recordId,
        string            recordType,
        Guid              staffUserId,
        string            newValue,
        string            justification,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        if (recordType == RecordTypeMedicalCode)
        {
            var code = await _db.MedicalCodes
                .FirstOrDefaultAsync(c => c.Id == recordId, ct);

            if (code is null) return null;

            string originalValue = code.CodeValue;

            code.OriginalCodeValue  = originalValue;   // preserve original AI value (AC-3)
            code.CodeValue          = newValue;
            code.VerificationStatus = CodeVerificationStatus.Overridden;
            code.VerifiedAt         = now;
            code.VerifiedByUserId   = staffUserId;
            code.OverrideJustification = justification;
            code.UpdatedAt          = now;

            _db.CodingAuditLogs.Add(new CodingAuditLog
            {
                MedicalCodeId = recordId,
                PatientId     = code.PatientId,
                Action        = CodingAuditAction.Overridden,
                OldCodeValue  = originalValue,
                NewCodeValue  = newValue,
                Justification = justification,
                UserId        = staffUserId,
                Timestamp     = DateTimeOffset.UtcNow,
                CreatedAt     = now,
            });

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "VerificationEnforcement: MedicalCode modified and approved. " +
                "RecordId={RecordId} StaffId={StaffId}.",
                recordId, staffUserId);

            return BuildAuditDto(recordId, RecordTypeMedicalCode, staffUserId, now,
                "Modified", originalValue, newValue, justification);
        }

        if (recordType == RecordTypeExtractedData)
        {
            var row = await _db.ExtractedData
                .FirstOrDefaultAsync(r => r.Id == recordId, ct);

            if (row is null) return null;

            string originalValue = DescribeExtractedData(row);

            row.VerificationStatus = VerificationStatus.Corrected;
            row.VerifiedByUserId   = staffUserId;
            row.VerifiedAtUtc      = now;
            row.FlaggedForReview   = false;
            row.UpdatedAt          = now;

            _db.AuditLogs.Add(BuildAuditLog(staffUserId, AuditAction.ExtractedDataVerified,
                RecordTypeExtractedData, recordId));

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "VerificationEnforcement: ExtractedData modified and approved. " +
                "RecordId={RecordId} StaffId={StaffId}.",
                recordId, staffUserId);

            return BuildAuditDto(recordId, RecordTypeExtractedData, staffUserId, now,
                "Modified", originalValue, newValue, justification);
        }

        _logger.LogWarning(
            "VerificationEnforcement: unknown RecordType '{RecordType}' for RecordId={RecordId}.",
            recordType, recordId);
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RejectAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<VerificationAuditEntryDto?> RejectAsync(
        Guid              recordId,
        string            recordType,
        Guid              staffUserId,
        string            reason,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        if (recordType == RecordTypeMedicalCode)
        {
            var code = await _db.MedicalCodes
                .FirstOrDefaultAsync(c => c.Id == recordId, ct);

            if (code is null) return null;

            string originalValue = code.CodeValue;

            code.VerificationStatus = CodeVerificationStatus.Rejected;
            code.VerifiedAt         = now;
            code.VerifiedByUserId   = staffUserId;
            code.OverrideJustification = reason;
            code.UpdatedAt          = now;

            _db.CodingAuditLogs.Add(new CodingAuditLog
            {
                MedicalCodeId = recordId,
                PatientId     = code.PatientId,
                Action        = CodingAuditAction.Rejected,
                OldCodeValue  = originalValue,
                NewCodeValue  = string.Empty,
                Justification = reason,
                UserId        = staffUserId,
                Timestamp     = DateTimeOffset.UtcNow,
                CreatedAt     = now,
            });

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "VerificationEnforcement: MedicalCode rejected. " +
                "RecordId={RecordId} StaffId={StaffId}.",
                recordId, staffUserId);

            return BuildAuditDto(recordId, RecordTypeMedicalCode, staffUserId, now,
                "Rejected", originalValue, string.Empty, reason);
        }

        if (recordType == RecordTypeExtractedData)
        {
            var row = await _db.ExtractedData
                .FirstOrDefaultAsync(r => r.Id == recordId, ct);

            if (row is null) return null;

            string originalValue = DescribeExtractedData(row);

            row.VerificationStatus = VerificationStatus.Rejected;
            row.VerifiedByUserId   = staffUserId;
            row.VerifiedAtUtc      = now;
            row.UpdatedAt          = now;

            _db.AuditLogs.Add(BuildAuditLog(staffUserId, AuditAction.DataModify,
                RecordTypeExtractedData, recordId));

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "VerificationEnforcement: ExtractedData rejected. " +
                "RecordId={RecordId} StaffId={StaffId}.",
                recordId, staffUserId);

            return BuildAuditDto(recordId, RecordTypeExtractedData, staffUserId, now,
                "Rejected", originalValue, string.Empty, reason);
        }

        _logger.LogWarning(
            "VerificationEnforcement: unknown RecordType '{RecordType}' for RecordId={RecordId}.",
            recordType, recordId);
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BatchApproveAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<VerificationAuditEntryDto>> BatchApproveAsync(
        IReadOnlyList<Guid> recordIds,
        string              recordType,
        Guid                staffUserId,
        CancellationToken   ct = default)
    {
        var results = new List<VerificationAuditEntryDto>(recordIds.Count);
        var now     = DateTime.UtcNow;

        if (recordType == RecordTypeMedicalCode)
        {
            var codes = await _db.MedicalCodes
                .Where(c => recordIds.Contains(c.Id)
                         && c.VerificationStatus == CodeVerificationStatus.Pending)
                .ToListAsync(ct);

            foreach (var code in codes)
            {
                string originalValue = code.CodeValue;

                code.VerificationStatus = CodeVerificationStatus.Verified;
                code.VerifiedAt         = now;
                code.VerifiedByUserId   = staffUserId;
                code.UpdatedAt          = now;

                // Individual audit entry per item — required per edge case (AC-3).
                _db.CodingAuditLogs.Add(new CodingAuditLog
                {
                    MedicalCodeId = code.Id,
                    PatientId     = code.PatientId,
                    Action        = CodingAuditAction.Approved,
                    OldCodeValue  = originalValue,
                    NewCodeValue  = originalValue,
                    UserId        = staffUserId,
                    Timestamp     = DateTimeOffset.UtcNow,
                    CreatedAt     = now,
                });

                results.Add(BuildAuditDto(code.Id, RecordTypeMedicalCode, staffUserId, now,
                    "Approved", originalValue, originalValue, null));
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "VerificationEnforcement: batch MedicalCode approval. " +
                "Requested={Requested} Approved={Approved} StaffId={StaffId}.",
                recordIds.Count, results.Count, staffUserId);
        }
        else if (recordType == RecordTypeExtractedData)
        {
            var rows = await _db.ExtractedData
                .Where(r => recordIds.Contains(r.Id)
                         && r.VerificationStatus == VerificationStatus.Pending)
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                string originalValue = DescribeExtractedData(row);

                row.VerificationStatus = VerificationStatus.Verified;
                row.VerifiedByUserId   = staffUserId;
                row.VerifiedAtUtc      = now;
                row.FlaggedForReview   = false;
                row.UpdatedAt          = now;

                // Individual audit entry per item — required per edge case (AC-3).
                _db.AuditLogs.Add(BuildAuditLog(staffUserId, AuditAction.ExtractedDataBulkVerified,
                    RecordTypeExtractedData, row.Id));

                results.Add(BuildAuditDto(row.Id, RecordTypeExtractedData, staffUserId, now,
                    "Approved", originalValue, originalValue, null));
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "VerificationEnforcement: batch ExtractedData approval. " +
                "Requested={Requested} Approved={Approved} StaffId={StaffId}.",
                recordIds.Count, results.Count, staffUserId);
        }
        else
        {
            _logger.LogWarning(
                "VerificationEnforcement: unknown RecordType '{RecordType}' for batch approve.",
                recordType);
        }

        return results;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IsVerifiedAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> IsVerifiedAsync(
        Guid              recordId,
        string            recordType,
        CancellationToken ct = default)
    {
        if (recordType == RecordTypeMedicalCode)
        {
            return await _db.MedicalCodes
                .Where(c => c.Id == recordId)
                .Select(c => (CodeVerificationStatus?)c.VerificationStatus)
                .FirstOrDefaultAsync(ct) switch
            {
                CodeVerificationStatus.Verified   => true,
                CodeVerificationStatus.Overridden => true,
                CodeVerificationStatus.Rejected   => true,
                _                                 => false,
            };
        }

        if (recordType == RecordTypeExtractedData)
        {
            return await _db.ExtractedData
                .Where(r => r.Id == recordId)
                .Select(r => (VerificationStatus?)r.VerificationStatus)
                .FirstOrDefaultAsync(ct) switch
            {
                VerificationStatus.Verified      => true,
                VerificationStatus.Corrected     => true,
                VerificationStatus.ManualVerified => true,
                VerificationStatus.Rejected       => true,
                _                                => false,
            };
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static VerificationAuditEntryDto BuildAuditDto(
        Guid    recordId,
        string  recordType,
        Guid    staffUserId,
        DateTime verifiedAt,
        string  action,
        string  originalAiValue,
        string  finalValue,
        string? justification)
        => new()
        {
            RecordId        = recordId,
            RecordType      = recordType,
            StaffUserId     = staffUserId,
            VerifiedAt      = verifiedAt,
            Action          = action,
            OriginalAiValue = originalAiValue,
            FinalValue      = finalValue,
            Justification   = justification,
        };

    private static AuditLog BuildAuditLog(
        Guid       userId,
        AuditAction action,
        string     resourceType,
        Guid       resourceId)
        => new()
        {
            LogId        = Guid.NewGuid(),
            UserId       = userId,
            Action       = action,
            ResourceType = resourceType,
            ResourceId   = resourceId,
            Timestamp    = DateTime.UtcNow,
            IpAddress    = string.Empty,  // not available in service layer
            UserAgent    = string.Empty,
        };

    /// <summary>
    /// Returns a non-PII descriptor string for an <see cref="ExtractedData"/> row
    /// suitable for the audit entry <c>OriginalAiValue</c> field.
    /// </summary>
    private static string DescribeExtractedData(UPACIP.DataAccess.Entities.ExtractedData row)
        => $"DataType={row.DataType} ConfidenceScore={row.ConfidenceScore:F2}";
}
