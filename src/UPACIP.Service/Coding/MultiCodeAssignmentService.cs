using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Coding;

/// <summary>
/// Assigns multiple ICD-10 and CPT codes to a patient encounter with individual
/// code verification and billing priority ordering (US_051, AC-3, FR-066).
///
/// Idempotency (NFR-034):
///   When an identical (patientId, codeType, codeValue) row already exists
///   the existing row is updated rather than duplicated.
///
/// Audit (FR-066, NFR-012):
///   Every assignment is recorded in <c>coding_audit_logs</c> with user attribution.
/// </summary>
public sealed class MultiCodeAssignmentService : IMultiCodeAssignmentService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext                  _db;
    private readonly ICacheService                         _cache;
    private readonly ILogger<MultiCodeAssignmentService>   _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public MultiCodeAssignmentService(
        ApplicationDbContext               db,
        ICacheService                      cache,
        ILogger<MultiCodeAssignmentService> logger)
    {
        _db     = db;
        _cache  = cache;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IMultiCodeAssignmentService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<MultiCodeAssignmentRunResult> AssignMultipleCodesAsync(
        Guid patientId,
        IReadOnlyList<CodeAssignmentItem> codes,
        Guid actingUserId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "MultiCodeAssignmentService: assigning {Count} codes for PatientId={PatientId} UserId={UserId}",
            codes.Count, patientId, actingUserId);

        var assignedItems = new List<AssignedCodeItem>(codes.Count);
        var auditLogs     = new List<CodingAuditLog>(codes.Count);

        for (int i = 0; i < codes.Count; i++)
        {
            var entry         = codes[i];
            var sequenceOrder = entry.SequenceOrder > 0 ? entry.SequenceOrder : i + 1;

            // Validate code format against library (DR-015)
            if (!await IsValidLibraryCodeAsync(entry.CodeType, entry.CodeValue, ct))
            {
                _logger.LogWarning(
                    "MultiCodeAssignmentService: code {CodeValue} not found in library. Assigning anyway.",
                    entry.CodeValue);
            }

            // Upsert (idempotent): find existing row for this patient + code
            var existing = await _db.MedicalCodes.FirstOrDefaultAsync(
                c => c.PatientId  == patientId
                  && c.CodeType   == entry.CodeType
                  && c.CodeValue  == entry.CodeValue, ct);

            if (existing is not null)
            {
                existing.Description    = entry.Description;
                existing.Justification  = entry.Justification;
                existing.SequenceOrder  = sequenceOrder;
                existing.ApprovedByUserId = actingUserId;
                existing.VerificationStatus = CodeVerificationStatus.Verified;
                existing.VerifiedByUserId = actingUserId;
                existing.VerifiedAt     = DateTime.UtcNow;

                assignedItems.Add(MapToItem(existing));
                auditLogs.Add(BuildAuditLog(existing.Id, existing.PatientId, actingUserId, CodingAuditAction.Approved, existing.CodeValue));
                continue;
            }

            var code = new MedicalCode
            {
                PatientId          = patientId,
                CodeType           = entry.CodeType,
                CodeValue          = entry.CodeValue,
                Description        = entry.Description,
                Justification      = entry.Justification,
                SuggestedByAi      = false,
                ApprovedByUserId   = actingUserId,
                VerificationStatus = CodeVerificationStatus.Verified,
                VerifiedByUserId   = actingUserId,
                VerifiedAt         = DateTime.UtcNow,
                SequenceOrder      = sequenceOrder,
            };

            _db.MedicalCodes.Add(code);
            await _db.SaveChangesAsync(ct);  // save to get the Id for audit log

            assignedItems.Add(MapToItem(code));
            auditLogs.Add(BuildAuditLog(code.Id, patientId, actingUserId, CodingAuditAction.Approved, code.CodeValue));
        }

        // Persist remaining changes and audit logs together
        _db.CodingAuditLogs.AddRange(auditLogs);

        // Invalidate pending-codes cache for the patient (NFR-030)
        await _cache.RemoveAsync($"pending-codes:{patientId}", ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "MultiCodeAssignmentService: assignment complete. PatientId={PatientId} Assigned={Count}",
            patientId, assignedItems.Count);

        return new MultiCodeAssignmentRunResult
        {
            PatientId     = patientId,
            AssignedCodes = assignedItems,
        };
    }

    /// <inheritdoc/>
    public async Task VerifySingleCodeAsync(
        Guid patientId,
        Guid codeId,
        Guid actingUserId,
        CancellationToken ct = default)
    {
        var code = await _db.MedicalCodes
            .FirstOrDefaultAsync(c => c.Id == codeId && c.PatientId == patientId, ct)
            ?? throw new KeyNotFoundException(
                $"MedicalCode {codeId} not found for PatientId {patientId}.");

        code.VerificationStatus = CodeVerificationStatus.Verified;
        code.VerifiedByUserId   = actingUserId;
        code.VerifiedAt         = DateTime.UtcNow;
        code.ApprovedByUserId   = actingUserId;

        _db.CodingAuditLogs.Add(
            BuildAuditLog(codeId, patientId, actingUserId, CodingAuditAction.Approved, code.CodeValue));

        await _db.SaveChangesAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<bool> IsValidLibraryCodeAsync(
        CodeType codeType, string codeValue, CancellationToken ct)
    {
        return codeType == CodeType.Icd10
            ? await _db.Icd10CodeLibrary
                .AnyAsync(c => c.CodeValue == codeValue && c.IsCurrent, ct)
            : await _db.CptCodeLibrary
                .AnyAsync(c => c.CptCode == codeValue && c.IsActive, ct);
    }

    private static AssignedCodeItem MapToItem(MedicalCode code) => new()
    {
        CodeId                = code.Id,
        CodeValue             = code.CodeValue,
        CodeType              = code.CodeType,
        Description           = code.Description,
        SequenceOrder         = code.SequenceOrder,
        PayerValidationStatus = code.PayerValidationStatus,
    };

    private static CodingAuditLog BuildAuditLog(
        Guid codeId, Guid patientId, Guid userId,
        CodingAuditAction action, string codeValue) => new()
    {
        MedicalCodeId = codeId,
        PatientId     = patientId,
        UserId        = userId,
        Action        = action,
        OldCodeValue  = codeValue,
        NewCodeValue  = codeValue,
        Justification = "Multi-code assignment",
        CreatedAt     = DateTime.UtcNow,
    };
}
