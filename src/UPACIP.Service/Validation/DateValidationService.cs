using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Consolidation;

namespace UPACIP.Service.Validation;

/// <summary>
/// Chronological plausibility validator for clinical event dates (US_046 AC-2, edge case).
///
/// Date extraction strategy:
///   Dates are read from <c>ExtractedDataContent.Metadata</c> using the key <c>"record_date"</c>.
///   Secondary keys <c>"procedure_date"</c>, <c>"diagnosis_date"</c>, <c>"admission_date"</c>,
///   <c>"discharge_date"</c>, and <c>"follow_up_date"</c> are also checked (fallback).
///
/// Partial-date detection:
///   <c>YYYY-MM</c> (year+month) and <c>YYYY</c> (year only) patterns are flagged as incomplete.
///   Full ISO-8601 dates (<c>YYYY-MM-DD</c>) are parsed and used for comparison.
///
/// Phase 1 timezone: all dates are treated as clinic-local; timezone suffixes are stripped before parsing.
/// </summary>
public sealed partial class DateValidationService : IDateValidationService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Regex — source-generated for performance
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Full ISO-8601 date: YYYY-MM-DD (with optional time/tz suffix that is stripped).</summary>
    [GeneratedRegex(@"^(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})", RegexOptions.Compiled)]
    private static partial Regex FullDateRegex();

    /// <summary>Partial date year+month: YYYY-MM (no day component).</summary>
    [GeneratedRegex(@"^\d{4}-\d{2}$", RegexOptions.Compiled)]
    private static partial Regex YearMonthRegex();

    /// <summary>Partial date year only: exactly four digits.</summary>
    [GeneratedRegex(@"^\d{4}$", RegexOptions.Compiled)]
    private static partial Regex YearOnlyRegex();

    // ─────────────────────────────────────────────────────────────────────────
    // Metadata keys searched in priority order
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly string[] GeneralDateKeys        = ["record_date", "date"];
    private static readonly string[] ProcedureDateKeys      = ["procedure_date", "record_date", "date"];
    private static readonly string[] DiagnosisDateKeys      = ["diagnosis_date", "record_date", "date"];
    private static readonly string[] AdmissionDateKeys      = ["admission_date", "record_date", "date"];
    private static readonly string[] DischargeDateKeys      = ["discharge_date", "record_date", "date"];
    private static readonly string[] FollowUpDateKeys       = ["follow_up_date", "date"];

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ILogger<DateValidationService> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public DateValidationService(ILogger<DateValidationService> logger)
    {
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IDateValidationService — ValidateAndAnnotateAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<IReadOnlyList<DateViolationDto>> ValidateAndAnnotateAsync(
        IEnumerable<ExtractedData> patientExtractedData,
        CancellationToken          ct = default)
    {
        var rows      = patientExtractedData.ToList();
        var violations = new List<DateViolationDto>();

        // ── 1. Partial-date flagging ─────────────────────────────────────────
        foreach (var row in rows)
        {
            var rawDate = ExtractDateString(row, GeneralDateKeys);
            if (rawDate is null) continue;

            if (YearMonthRegex().IsMatch(rawDate) || YearOnlyRegex().IsMatch(rawDate))
            {
                row.IsIncompleteDate = true;
                var explanation = $"Extracted date '{rawDate}' is incomplete (month or day is missing). Staff must complete the date.";
                row.DateConflictExplanation = explanation;

                violations.Add(new DateViolationDto
                {
                    ExtractedDataId  = row.Id,
                    DataType         = row.DataType,
                    Explanation      = explanation,
                    IsIncompleteDate = true,
                });
            }
        }

        // ── 2. Chronological rules — only evaluate full dates ────────────────
        var procedures  = ParseFullDates(rows, DataType.Procedure,  ProcedureDateKeys);
        var diagnoses   = ParseFullDates(rows, DataType.Diagnosis,  DiagnosisDateKeys);

        // Rule A: Procedure date must not precede the earliest diagnosis date for the patient.
        if (procedures.Count > 0 && diagnoses.Count > 0)
        {
            var earliestDiagnosis = diagnoses.Min(x => x.Date);
            foreach (var (row, date) in procedures)
            {
                if (date < earliestDiagnosis)
                {
                    var explanation =
                        $"Procedure '{row.DataContent?.NormalizedValue ?? row.Id.ToString()}' " +
                        $"dated {date:yyyy-MM-dd} precedes earliest diagnosis dated {earliestDiagnosis:yyyy-MM-dd}. " +
                        "Verify event ordering is clinically correct.";

                    row.DateConflictExplanation = explanation;
                    violations.Add(new DateViolationDto
                    {
                        ExtractedDataId  = row.Id,
                        DataType         = DataType.Procedure,
                        Explanation      = explanation,
                        IsIncompleteDate = false,
                    });
                }
            }
        }

        // Rule B: Within the same document, discharge date must follow admission date.
        var admissionsByDoc = rows
            .Where(r => r.DataType == DataType.Procedure)
            .GroupBy(r => r.DocumentId);

        foreach (var group in admissionsByDoc)
        {
            var admissionDates = group
                .Select(r => (Row: r, Date: TryParseFullDate(ExtractDateString(r, AdmissionDateKeys))))
                .Where(x => x.Date.HasValue)
                .ToList();

            var dischargeDates = group
                .Select(r => (Row: r, Date: TryParseFullDate(ExtractDateString(r, DischargeDateKeys))))
                .Where(x => x.Date.HasValue)
                .ToList();

            foreach (var adm in admissionDates)
            {
                foreach (var dis in dischargeDates)
                {
                    if (dis.Date!.Value < adm.Date!.Value)
                    {
                        var explanation =
                            $"Discharge date {dis.Date.Value:yyyy-MM-dd} precedes admission date " +
                            $"{adm.Date.Value:yyyy-MM-dd} in document '{adm.Row.Document?.OriginalFileName ?? group.Key.ToString()}'. " +
                            "Verify the date ordering is correct.";

                        dis.Row.DateConflictExplanation = explanation;
                        violations.Add(new DateViolationDto
                        {
                            ExtractedDataId  = dis.Row.Id,
                            DataType         = dis.Row.DataType,
                            Explanation      = explanation,
                            IsIncompleteDate = false,
                        });
                    }
                }
            }
        }

        // Rule C: Follow-up date must follow initial visit date within the same document.
        foreach (var docGroup in rows.GroupBy(r => r.DocumentId))
        {
            var initialDates = docGroup
                .Select(r => (Row: r, Date: TryParseFullDate(ExtractDateString(r, GeneralDateKeys))))
                .Where(x => x.Date.HasValue)
                .OrderBy(x => x.Date)
                .FirstOrDefault();

            if (initialDates.Row is null) continue;

            foreach (var row in docGroup)
            {
                if (row.Id == initialDates.Row.Id) continue;

                var followUpRaw  = ExtractDateString(row, FollowUpDateKeys);
                var followUpDate = TryParseFullDate(followUpRaw);
                if (followUpDate.HasValue && followUpDate.Value < initialDates.Date!.Value)
                {
                    var explanation =
                        $"Follow-up date {followUpDate.Value:yyyy-MM-dd} precedes initial visit " +
                        $"date {initialDates.Date.Value:yyyy-MM-dd}. Verify event ordering.";

                    row.DateConflictExplanation ??= explanation;
                    violations.Add(new DateViolationDto
                    {
                        ExtractedDataId  = row.Id,
                        DataType         = row.DataType,
                        Explanation      = explanation,
                        IsIncompleteDate = false,
                    });
                }
            }
        }

        _logger.LogDebug(
            "DateValidationService: validation complete. RowCount={Rows}, Violations={Violations}",
            rows.Count, violations.Count);

        return Task.FromResult<IReadOnlyList<DateViolationDto>>(violations);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static List<(ExtractedData Row, DateTime Date)> ParseFullDates(
        IEnumerable<ExtractedData> rows,
        DataType                   dataType,
        string[]                   keys)
    {
        return rows
            .Where(r => r.DataType == dataType)
            .Select(r => (Row: r, DateStr: ExtractDateString(r, keys)))
            .Where(x => x.DateStr is not null)
            .Select(x => (x.Row, Date: TryParseFullDate(x.DateStr)))
            .Where(x => x.Date.HasValue)
            .Select(x => (x.Row, x.Date!.Value))
            .ToList();
    }

    private static string? ExtractDateString(ExtractedData row, string[] keys)
    {
        if (row.DataContent?.Metadata is null) return null;

        foreach (var key in keys)
        {
            if (row.DataContent.Metadata.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
                return val.Trim();
        }

        return null;
    }

    private static DateTime? TryParseFullDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var match = FullDateRegex().Match(raw);
        if (!match.Success) return null;

        if (DateTime.TryParse(
                $"{match.Groups["y"].Value}-{match.Groups["m"].Value}-{match.Groups["d"].Value}",
                out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
