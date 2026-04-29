using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using UPACIP.Service.PatientRights.Models;

namespace UPACIP.Service.PatientRights.Export;

/// <summary>
/// Generates a structured, human-readable JSON export of all patient data categories
/// (US_094, AC-2). Output follows a labeled top-level structure for machine readability.
/// </summary>
public sealed class JsonExportGenerator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented              = true,
        PropertyNamingPolicy       = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition     = JsonIgnoreCondition.WhenWritingNull,
        Converters                 = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly ILogger<JsonExportGenerator> _logger;

    public JsonExportGenerator(ILogger<JsonExportGenerator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Serializes the patient data package to a labeled JSON structure and returns
    /// the UTF-8 encoded byte array.
    /// </summary>
    public Task<byte[]> GenerateAsync(PatientDataPackage data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var envelope = new
        {
            exportMetadata = new
            {
                exportedAt = data.ExportedAtUtc,
                version    = data.ExportVersion,
                patientId  = data.PatientId,
                notice     = "This export is provided under HIPAA §164.524 — Right of Access.",
            },
            patientProfile    = data.Profile,
            appointments      = data.Appointments,
            intakeRecords     = data.IntakeRecords,
            clinicalDocuments = data.ClinicalDocuments,
            medicalCodes      = data.MedicalCodes,
        };

        var json  = JsonSerializer.Serialize(envelope, SerializerOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        _logger.LogDebug(
            "JsonExportGenerator: generated {Bytes} bytes for patient {PatientId}.",
            bytes.Length,
            data.PatientId);

        return Task.FromResult(bytes);
    }
}
