using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.Service.PatientRights.Models;

namespace UPACIP.Service.PatientRights.DataCollectors;

/// <summary>
/// Collects intake data, clinical documents (with extracted data), and medical codes for a
/// patient (US_094, AC-1).
/// </summary>
public sealed class ClinicalDataCollector
{
    private readonly ApplicationDbContext           _db;
    private readonly ILogger<ClinicalDataCollector> _logger;

    public ClinicalDataCollector(
        ApplicationDbContext           db,
        ILogger<ClinicalDataCollector> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <summary>
    /// Collects intake records, clinical documents with extracted data, and medical codes.
    /// Empty collections are returned for categories with no data — all are valid scenarios.
    /// </summary>
    public async Task<ClinicalDataPackage> CollectAsync(Guid patientId, CancellationToken ct)
    {
        var intakeRecordsTask    = CollectIntakeRecordsAsync(patientId, ct);
        var clinicalDocsTask     = CollectClinicalDocumentsAsync(patientId, ct);
        var medicalCodesTask     = CollectMedicalCodesAsync(patientId, ct);

        await Task.WhenAll(intakeRecordsTask, clinicalDocsTask, medicalCodesTask);

        var package = new ClinicalDataPackage
        {
            IntakeRecords     = await intakeRecordsTask,
            ClinicalDocuments = await clinicalDocsTask,
            MedicalCodes      = await medicalCodesTask,
        };

        _logger.LogDebug(
            "ClinicalDataCollector: patient {PatientId} — {I} intake, {D} documents, {M} codes.",
            patientId,
            package.IntakeRecords.Count,
            package.ClinicalDocuments.Count,
            package.MedicalCodes.Count);

        return package;
    }

    private async Task<List<IntakeDataRecord>> CollectIntakeRecordsAsync(Guid patientId, CancellationToken ct)
    {
        var records = await _db.IntakeRecords
            .AsNoTracking()
            .Where(i => i.PatientId == patientId)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);

        return records.Select(i => new IntakeDataRecord
        {
            IntakeDataId   = i.Id,
            IntakeMethod   = i.IntakeMethod.ToString(),
            MandatoryFields = i.MandatoryFields,
            OptionalFields  = i.OptionalFields,
            InsuranceInfo   = i.InsuranceInfo,
            CompletedAt     = i.CompletedAt,
            CreatedAt       = i.CreatedAt,
        }).ToList();
    }

    private async Task<List<ClinicalDocumentData>> CollectClinicalDocumentsAsync(Guid patientId, CancellationToken ct)
    {
        var documents = await _db.ClinicalDocuments
            .AsNoTracking()
            .Where(d => d.PatientId == patientId)
            .Include(d => d.ExtractedData)
            .OrderBy(d => d.UploadDate)
            .ToListAsync(ct);

        return documents.Select(d => new ClinicalDocumentData
        {
            DocumentId       = d.Id,
            DocumentCategory = d.DocumentCategory.ToString(),
            OriginalFileName = d.OriginalFileName,
            UploadDate       = d.UploadDate,
            ProcessingStatus = d.ProcessingStatus.ToString(),
            ExtractedData    = d.ExtractedData.Select(e => new ExtractedDataRecord
            {
                ExtractedDataId          = e.Id,
                DataType                 = e.DataType.ToString(),
                DataContent              = e.DataContent,
                ConfidenceScore          = e.ConfidenceScore,
                CalibratedConfidenceScore = e.CalibratedConfidenceScore,
                SourceAttribution        = e.SourceAttribution,
                PageNumber               = e.PageNumber,
            }).ToList(),
        }).ToList();
    }

    private async Task<List<MedicalCodeData>> CollectMedicalCodesAsync(Guid patientId, CancellationToken ct)
    {
        var codes = await _db.MedicalCodes
            .AsNoTracking()
            .Where(m => m.PatientId == patientId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        return codes.Select(m => new MedicalCodeData
        {
            MedicalCodeId     = m.Id,
            CodeType          = m.CodeType.ToString(),
            CodeValue         = m.CodeValue,
            Description       = m.Description,
            Justification     = m.Justification,
            SuggestedByAi     = m.SuggestedByAi,
            AiConfidenceScore = m.AiConfidenceScore,
            CreatedAt         = m.CreatedAt,
        }).ToList();
    }
}
