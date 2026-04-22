using UPACIP.DataAccess.Entities;
using UPACIP.Service.Consolidation;

namespace UPACIP.Service.Validation;

/// <summary>
/// Validates clinical event dates for chronological plausibility (US_046 AC-2, edge case).
///
/// Rules evaluated:
/// <list type="number">
///   <item>Procedure date must not precede any Diagnosis date for the same patient.</item>
///   <item>Discharge date must follow admission date within the same source document.</item>
///   <item>Follow-up appointment date must follow the initial visit date for the same document.</item>
///   <item>Partial dates (month/year or year-only) are flagged as "incomplete-date" without blocking.</item>
/// </list>
///
/// Phase 1 timezone strategy: all dates are treated as clinic-local (timezone metadata stripped).
/// </summary>
public interface IDateValidationService
{
    /// <summary>
    /// Validates the extracted-data entries for a patient, detects chronological violations,
    /// flags partial dates, and persists the violations back to the <c>ExtractedData</c> rows
    /// via <c>DateConflictExplanation</c> and <c>IsIncompleteDate</c>.
    /// </summary>
    /// <param name="patientExtractedData">
    /// All non-archived <c>ExtractedData</c> rows for the patient, including the <c>Document</c>
    /// navigation property (caller must <c>.Include(ed => ed.Document)</c>).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// List of violations found. The service also mutates <paramref name="patientExtractedData"/> in-place
    /// so the caller's EF Core change tracker can persist the updates in one <c>SaveChangesAsync</c>.
    /// </returns>
    Task<IReadOnlyList<DateViolationDto>> ValidateAndAnnotateAsync(
        IEnumerable<ExtractedData> patientExtractedData,
        CancellationToken          ct = default);
}
