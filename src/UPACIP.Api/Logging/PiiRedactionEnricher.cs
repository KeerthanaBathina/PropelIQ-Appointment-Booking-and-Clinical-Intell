using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Events;
using UPACIP.Api.Configuration;

namespace UPACIP.Api.Logging;

/// <summary>
/// Serilog <see cref="ILogEventEnricher"/> that masks PII in scalar log properties
/// before the event reaches any sink (US_066 AC-2, NFR-017, EC-2).
///
/// Redaction is triggered by two signals (in priority order):
///   1. Property name matches a configured field name (config-driven — EC-2).
///   2. Property value matches a known PII regex pattern (email, phone, SSN).
///
/// Only <see cref="ScalarValue"/> properties are examined.
/// Non-scalar properties (sequences, structures) are handled by
/// <see cref="PiiDestructuringPolicy"/>.
/// </summary>
public sealed class PiiRedactionEnricher : ILogEventEnricher
{
    private readonly PiiRedactionOptions _options;

    /// <summary>Pre-computed lower-cased set for O(1) field-name lookup.</summary>
    private readonly HashSet<string> _piiFieldNames;

    public PiiRedactionEnricher(IOptions<PiiRedactionOptions> options)
    {
        _options      = options.Value;
        _piiFieldNames = new HashSet<string>(
            _options.PiiFieldNames.Select(n => n.ToLowerInvariant()),
            StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent is null) return;

        // Enumerate a snapshot to avoid mutating the dictionary while iterating
        var properties = logEvent.Properties.ToList();

        foreach (var (key, value) in properties)
        {
            if (value is not ScalarValue scalar) continue;
            if (scalar.Value is not string stringValue) continue;
            if (string.IsNullOrWhiteSpace(stringValue)) continue;

            var maskedValue = TryRedact(key, stringValue);
            if (maskedValue is null) continue; // Not a PII field — skip

            // Replace the original scalar with the masked value in-place
            logEvent.AddOrUpdateProperty(
                propertyFactory.CreateProperty(key, maskedValue));
        }
    }

    /// <summary>
    /// Returns the masked value if <paramref name="propertyName"/> or
    /// <paramref name="value"/> indicates PII; otherwise <see langword="null"/>.
    /// </summary>
    private string? TryRedact(string propertyName, string value)
    {
        // 1 — Field-name match (config-driven EC-2)
        if (_piiFieldNames.Contains(propertyName.ToLowerInvariant()))
        {
            return RedactByFieldName(propertyName, value);
        }

        // 2 — Pattern-based detection on unstructured values
        if (PiiMaskingPatterns.EmailRegex.IsMatch(value)) return PiiMaskingPatterns.MaskEmail(value);
        if (PiiMaskingPatterns.SsnRegex.IsMatch(value))   return PiiMaskingPatterns.MaskSsn(value);
        if (PiiMaskingPatterns.PhoneRegex.IsMatch(value)) return PiiMaskingPatterns.MaskPhone(value);

        return null;
    }

    /// <summary>
    /// Selects the masking algorithm based on the semantic of <paramref name="fieldName"/>.
    /// </summary>
    private static string RedactByFieldName(string fieldName, string value)
    {
        var lc = fieldName.ToLowerInvariant();

        if (lc is "email")                                          return PiiMaskingPatterns.MaskEmail(value);
        if (lc is "phonenumber" or "phone")                        return PiiMaskingPatterns.MaskPhone(value);
        if (lc is "ssn" or "socialsecuritynumber")                 return PiiMaskingPatterns.MaskSsn(value);
        if (lc is "dateofbirth" or "dob" or "birthdate")           return PiiMaskingPatterns.MaskDateOfBirth(value);
        if (lc is "firstname" or "lastname"
                or "patientname" or "fullname" or "name")          return PiiMaskingPatterns.MaskName(value);

        return PiiMaskingPatterns.MaskGeneric(value);
    }
}
