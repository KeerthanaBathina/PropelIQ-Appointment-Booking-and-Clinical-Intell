using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities.OwnedTypes;

namespace UPACIP.Service.PatientRights.Deletion;

/// <summary>
/// Anonymizes patient data that is shared with other entities or consolidated clinical views
/// rather than deleting it (US_094, AC-3, edge case 2).
///
/// Anonymization strategy:
///   - ExtractedData linked to staff-uploaded documents: replace PII content with "[ANONYMIZED]"
///     but retain the clinical data structure for aggregate analytics.
///   - MedicalCodes approved by staff: preserve the code/approval record for the staff audit
///     trail but null-out the PatientId FK so the patient cannot be re-identified.
/// </summary>
public sealed class SharedDataAnonymizer
{
    private readonly ApplicationDbContext           _db;
    private readonly ILogger<SharedDataAnonymizer>  _logger;

    public SharedDataAnonymizer(
        ApplicationDbContext          db,
        ILogger<SharedDataAnonymizer> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <summary>
    /// Anonymizes shared/consolidated data for the patient and returns the count of
    /// anonymized records.
    /// </summary>
    public async Task<int> AnonymizeSharedDataAsync(Guid patientId, CancellationToken ct)
    {
        var count = 0;

        count += await AnonymizeStaffUploadedExtractedDataAsync(patientId, ct);
        count += await AnonymizeStaffApprovedMedicalCodesAsync(patientId, ct);

        _logger.LogInformation(
            "DELETION_DATA_ANONYMIZED: PatientId={PatientId}, RecordsAnonymized={Count}",
            patientId, count);

        return count;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task<int> AnonymizeStaffUploadedExtractedDataAsync(Guid patientId, CancellationToken ct)
    {
        // Staff-uploaded documents: UploaderUserId is always set — documents that were
        // uploaded by a staff/admin user have a shared clinical context and should be
        // anonymized rather than deleted.
        var staffDocumentIds = await _db.ClinicalDocuments
            .Where(d => d.PatientId == patientId && d.UploaderUserId != Guid.Empty)
            .Select(d => d.Id)
            .ToListAsync(ct);

        if (staffDocumentIds.Count == 0)
            return 0;

        var extractions = await _db.ExtractedData
            .Where(e => staffDocumentIds.Contains(e.DocumentId))
            .ToListAsync(ct);

        if (extractions.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        foreach (var ext in extractions)
        {
            // Replace PII content while preserving clinical structure.
            ext.DataContent = new ExtractedDataContent
            {
                RawText         = "[ANONYMIZED]",
                NormalizedValue = ext.DataContent?.NormalizedValue,  // retain clinical value
                Unit            = ext.DataContent?.Unit,
                SourceSnippet   = "[ANONYMIZED]",
                Metadata        = new Dictionary<string, string>
                    { ["anonymized_at"] = now.ToString("O"), ["reason"] = "patient_deletion" },
            };
            ext.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        return extractions.Count;
    }

    private async Task<int> AnonymizeStaffApprovedMedicalCodesAsync(Guid patientId, CancellationToken ct)
    {
        // Medical codes with an approving staff member: the code/approval pairing is a staff
        // audit record. Null-out the patient FK so the patient cannot be re-identified, but
        // preserve the code value and approval for the staff audit trail.
        var approvedCodes = await _db.MedicalCodes
            .Where(m => m.PatientId == patientId && m.ApprovedByUserId != null)
            .ToListAsync(ct);

        if (approvedCodes.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        foreach (var code in approvedCodes)
        {
            // Set PatientId to Empty GUID — FK constraint allows this as a sentinel for
            // "patient deleted" without breaking the EF mapping. The patient row will be
            // soft-deleted with PII wiped in phase 4.
            code.PatientId  = Guid.Empty;
            code.UpdatedAt  = now;
        }

        await _db.SaveChangesAsync(ct);
        return approvedCodes.Count;
    }
}
