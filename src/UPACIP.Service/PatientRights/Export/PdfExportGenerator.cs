using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using UPACIP.Service.PatientRights.Models;

namespace UPACIP.Service.PatientRights.Export;

/// <summary>
/// Generates a human-readable PDF export of all patient data categories using QuestPDF
/// (US_094, AC-2).  The document includes a cover page, table of contents, and five
/// labeled data sections.
/// </summary>
public sealed class PdfExportGenerator
{
    private readonly ILogger<PdfExportGenerator> _logger;

    public PdfExportGenerator(ILogger<PdfExportGenerator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates the patient data export PDF and returns it as a byte array.
    /// </summary>
    public Task<byte[]> GenerateAsync(PatientDataPackage data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var document = new PatientDataDocument(data);
        var bytes    = document.GeneratePdf();

        _logger.LogDebug(
            "PdfExportGenerator: generated {Bytes} bytes for patient {PatientId}.",
            bytes.Length,
            data.PatientId);

        return Task.FromResult(bytes);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// QuestPDF document definition
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class PatientDataDocument : IDocument
{
    private readonly PatientDataPackage _data;

    private static readonly TextStyle HeaderStyle  = TextStyle.Default.FontSize(18).Bold().FontColor(Colors.Blue.Darken3);
    private static readonly TextStyle SectionStyle = TextStyle.Default.FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
    private static readonly TextStyle BodyStyle    = TextStyle.Default.FontSize(10);
    private static readonly TextStyle LabelStyle   = TextStyle.Default.FontSize(10).Bold();
    private static readonly TextStyle SmallStyle   = TextStyle.Default.FontSize(8).FontColor(Colors.Grey.Darken2);

    public PatientDataDocument(PatientDataPackage data) => _data = data;

    public DocumentMetadata GetMetadata() => new()
    {
        Title       = "Patient Data Export",
        Subject     = "HIPAA Right of Access",
        Author      = "UPACIP Platform",
        CreationDate = _data.ExportedAtUtc,
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(40);
            page.Size(PageSizes.A4);
            page.DefaultTextStyle(BodyStyle);

            page.Header().Element(ComposeCoverHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeCoverHeader(IContainer c) =>
        c.PaddingBottom(10).BorderBottom(1).BorderColor(Colors.Blue.Darken3).Column(col =>
        {
            col.Item().Text("Patient Data Export Report").Style(HeaderStyle);
            col.Item().Text($"Patient: {_data.Profile.FullName}").Style(BodyStyle);
            col.Item().Text($"Export Date: {_data.ExportedAtUtc:yyyy-MM-dd HH:mm:ss} UTC").Style(BodyStyle);
            col.Item().PaddingTop(4).Text(
                "This document is provided in accordance with HIPAA §164.524 — Right of Access.")
                .Style(SmallStyle).Italic();
        });

    private void ComposeContent(IContainer c) => c.Column(col =>
    {
        col.Spacing(16);

        // Table of Contents
        col.Item().Element(ComposeToc);

        // Section 1 — Patient Profile
        col.Item().Element(ComposePatientProfile);

        // Section 2 — Appointments
        col.Item().Element(ComposeAppointments);

        // Section 3 — Intake Records
        col.Item().Element(ComposeIntakeRecords);

        // Section 4 — Clinical Documents
        col.Item().Element(ComposeClinicalDocuments);

        // Section 5 — Medical Codes
        col.Item().Element(ComposeMedicalCodes);
    });

    private void ComposeToc(IContainer c) => c.Column(col =>
    {
        col.Item().Text("Table of Contents").Style(SectionStyle);
        col.Item().PaddingTop(4).Column(inner =>
        {
            inner.Item().Text("1. Patient Profile").Style(BodyStyle);
            inner.Item().Text("2. Appointments").Style(BodyStyle);
            inner.Item().Text("3. Intake Records").Style(BodyStyle);
            inner.Item().Text("4. Clinical Documents").Style(BodyStyle);
            inner.Item().Text("5. Medical Codes").Style(BodyStyle);
        });
    });

    // ── Section 1 ────────────────────────────────────────────────────────────

    private void ComposePatientProfile(IContainer c) => c.Column(col =>
    {
        col.Item().Text("Section 1 — Patient Profile").Style(SectionStyle);
        col.Item().PaddingTop(4).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(140);
                cols.RelativeColumn();
            });

            AddRow(table, "Name",              _data.Profile.FullName);
            AddRow(table, "Email",             _data.Profile.Email);
            AddRow(table, "Date of Birth",     _data.Profile.DateOfBirth.ToString("yyyy-MM-dd"));
            AddRow(table, "Phone Number",      _data.Profile.PhoneNumber);
            AddRow(table, "Emergency Contact", _data.Profile.EmergencyContact ?? "—");
            AddRow(table, "Account Created",   _data.Profile.CreatedAt.ToString("yyyy-MM-dd"));
        });
    });

    // ── Section 2 ────────────────────────────────────────────────────────────

    private void ComposeAppointments(IContainer c) => c.Column(col =>
    {
        col.Item().Text("Section 2 — Appointments").Style(SectionStyle);

        if (_data.Appointments.Count == 0)
        {
            col.Item().PaddingTop(4).Text("No appointments found.").Style(BodyStyle);
            return;
        }

        col.Item().PaddingTop(4).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(2);
                cols.RelativeColumn();
                cols.RelativeColumn();
                cols.RelativeColumn();
            });

            // Header row
            table.Header(h =>
            {
                h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text("Date/Time").Style(LabelStyle);
                h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text("Status").Style(LabelStyle);
                h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text("Walk-In").Style(LabelStyle);
                h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text("Queue Wait (min)").Style(LabelStyle);
            });

            foreach (var appt in _data.Appointments)
            {
                var waitMin = appt.QueueEntries.FirstOrDefault()?.WaitTimeMinutes.ToString() ?? "—";
                table.Cell().Padding(4).Text(appt.AppointmentTime.ToString("yyyy-MM-dd HH:mm")).Style(BodyStyle);
                table.Cell().Padding(4).Text(appt.Status).Style(BodyStyle);
                table.Cell().Padding(4).Text(appt.IsWalkIn ? "Yes" : "No").Style(BodyStyle);
                table.Cell().Padding(4).Text(waitMin).Style(BodyStyle);
            }
        });
    });

    // ── Section 3 ────────────────────────────────────────────────────────────

    private void ComposeIntakeRecords(IContainer c) => c.Column(col =>
    {
        col.Item().Text("Section 3 — Intake Records").Style(SectionStyle);

        if (_data.IntakeRecords.Count == 0)
        {
            col.Item().PaddingTop(4).Text("No intake records found.").Style(BodyStyle);
            return;
        }

        foreach (var record in _data.IntakeRecords)
        {
            col.Item().PaddingTop(6).Column(inner =>
            {
                inner.Item().Text($"Intake Method: {record.IntakeMethod}  |  Completed: {record.CompletedAt?.ToString("yyyy-MM-dd") ?? "Pending"}").Style(LabelStyle);
                if (record.MandatoryFields is not null)
                    inner.Item().Text($"Mandatory Fields: {System.Text.Json.JsonSerializer.Serialize(record.MandatoryFields)}").Style(SmallStyle);
                if (record.OptionalFields is not null)
                    inner.Item().Text($"Optional Fields: {System.Text.Json.JsonSerializer.Serialize(record.OptionalFields)}").Style(SmallStyle);
                if (record.InsuranceInfo is not null)
                    inner.Item().Text($"Insurance Info: {System.Text.Json.JsonSerializer.Serialize(record.InsuranceInfo)}").Style(SmallStyle);
            });
        }
    });

    // ── Section 4 ────────────────────────────────────────────────────────────

    private void ComposeClinicalDocuments(IContainer c) => c.Column(col =>
    {
        col.Item().Text("Section 4 — Clinical Documents").Style(SectionStyle);

        if (_data.ClinicalDocuments.Count == 0)
        {
            col.Item().PaddingTop(4).Text("No clinical documents found.").Style(BodyStyle);
            return;
        }

        col.Item().PaddingTop(4).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(2);
                cols.RelativeColumn(2);
                cols.RelativeColumn();
                cols.RelativeColumn();
            });

            table.Header(h =>
            {
                h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text("File Name").Style(LabelStyle);
                h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text("Category").Style(LabelStyle);
                h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text("Upload Date").Style(LabelStyle);
                h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text("Status").Style(LabelStyle);
            });

            foreach (var doc in _data.ClinicalDocuments)
            {
                table.Cell().Padding(4).Text(doc.OriginalFileName).Style(BodyStyle);
                table.Cell().Padding(4).Text(doc.DocumentCategory).Style(BodyStyle);
                table.Cell().Padding(4).Text(doc.UploadDate.ToString("yyyy-MM-dd")).Style(BodyStyle);
                table.Cell().Padding(4).Text(doc.ProcessingStatus).Style(BodyStyle);
            }
        });

        // Extracted data sub-tables
        foreach (var doc in _data.ClinicalDocuments.Where(d => d.ExtractedData.Count > 0))
        {
            col.Item().PaddingTop(6).Column(inner =>
            {
                inner.Item().Text($"Extracted Data — {doc.OriginalFileName}").Style(LabelStyle);
                inner.Item().PaddingTop(2).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn();
                        cols.RelativeColumn(3);
                        cols.ConstantColumn(80);
                    });

                    table.Header(h =>
                    {
                        h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Data Type").Style(LabelStyle);
                        h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Source Attribution").Style(LabelStyle);
                        h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Confidence").Style(LabelStyle);
                    });

                    foreach (var ext in doc.ExtractedData)
                    {
                        table.Cell().Padding(4).Text(ext.DataType).Style(BodyStyle);
                        table.Cell().Padding(4).Text(ext.SourceAttribution).Style(BodyStyle);
                        table.Cell().Padding(4).Text($"{ext.ConfidenceScore:P0}").Style(BodyStyle);
                    }
                });
            });
        }
    });

    // ── Section 5 ────────────────────────────────────────────────────────────

    private void ComposeMedicalCodes(IContainer c) => c.Column(col =>
    {
        col.Item().Text("Section 5 — Medical Codes").Style(SectionStyle);

        if (_data.MedicalCodes.Count == 0)
        {
            col.Item().PaddingTop(4).Text("No medical codes found.").Style(BodyStyle);
            return;
        }

        col.Item().PaddingTop(4).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(70);
                cols.ConstantColumn(80);
                cols.RelativeColumn(2);
                cols.RelativeColumn(3);
                cols.ConstantColumn(80);
            });

            table.Header(h =>
            {
                h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text("Type").Style(LabelStyle);
                h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text("Code").Style(LabelStyle);
                h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text("Description").Style(LabelStyle);
                h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text("Justification").Style(LabelStyle);
                h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text("AI Confidence").Style(LabelStyle);
            });

            foreach (var code in _data.MedicalCodes)
            {
                var confidence = code.AiConfidenceScore.HasValue
                    ? $"{code.AiConfidenceScore.Value:P0}"
                    : "Manual";

                table.Cell().Padding(4).Text(code.CodeType).Style(BodyStyle);
                table.Cell().Padding(4).Text(code.CodeValue).Style(BodyStyle);
                table.Cell().Padding(4).Text(code.Description).Style(BodyStyle);
                table.Cell().Padding(4).Text(code.Justification).Style(BodyStyle);
                table.Cell().Padding(4).Text(confidence).Style(BodyStyle);
            }
        });
    });

    // ── Footer ───────────────────────────────────────────────────────────────

    private void ComposeFooter(IContainer c) => c
        .PaddingTop(8).BorderTop(1).BorderColor(Colors.Grey.Lighten1)
        .Row(row =>
        {
            row.RelativeItem().Text(
                $"Confidential — Protected Health Information | Export: {_data.ExportedAtUtc:yyyy-MM-dd HH:mm} UTC")
                .Style(SmallStyle);
            row.ConstantItem(60).AlignRight().Text(text =>
            {
                text.Span("Page ").Style(SmallStyle);
                text.CurrentPageNumber().Style(SmallStyle);
                text.Span(" of ").Style(SmallStyle);
                text.TotalPages().Style(SmallStyle);
            });
        });

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void AddRow(TableDescriptor table, string label, string value)
    {
        table.Cell().Padding(4).Background(Colors.Grey.Lighten4).Text(label).Style(
            TextStyle.Default.FontSize(10).Bold());
        table.Cell().Padding(4).Text(value).Style(TextStyle.Default.FontSize(10));
    }
}
