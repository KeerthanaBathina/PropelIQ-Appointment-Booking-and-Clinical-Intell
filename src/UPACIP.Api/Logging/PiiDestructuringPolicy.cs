using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Events;
using UPACIP.Api.Configuration;

namespace UPACIP.Api.Logging;

/// <summary>
/// Serilog <see cref="IDestructuringPolicy"/> that masks PII in structured
/// objects captured with the <c>{@Object}</c> operator (US_066 EC-2, AC-2, NFR-017).
///
/// When Serilog destructures an object the policy inspects each property:
///   - PII field names (config-driven) → field-name–aware masking.
///   - String values matching known PII patterns → pattern-based masking.
///
/// Non-string scalar values, sequences, and nested structures are left intact
/// to avoid recursion depth issues; nested PII is caught at the enricher level
/// once the flattened properties surface in <see cref="PiiRedactionEnricher"/>.
/// </summary>
public sealed class PiiDestructuringPolicy : IDestructuringPolicy
{
    private readonly PiiRedactionOptions _options;
    private readonly HashSet<string> _piiFieldNames;

    public PiiDestructuringPolicy(IOptions<PiiRedactionOptions> options)
    {
        _options       = options.Value;
        _piiFieldNames = new HashSet<string>(
            _options.PiiFieldNames.Select(n => n.ToLowerInvariant()),
            StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public bool TryDestructure(
        object? value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue result)
    {
        if (value is null)
        {
            result = new ScalarValue(null);
            return false; // Let Serilog handle null natively
        }

        var type       = value.GetType();
        var properties = type.GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        // Only intercept types that have at least one PII-named property
        var hasPiiField = properties.Any(p => _piiFieldNames.Contains(p.Name.ToLowerInvariant()));
        if (!hasPiiField)
        {
            result = new ScalarValue(null);
            return false; // Let Serilog's default destructuring proceed
        }

        // Rebuild the structure with PII fields masked
        var logProps = new List<LogEventProperty>(properties.Length);

        foreach (var prop in properties)
        {
            string propName;
            LogEventPropertyValue logValue;

            try
            {
                var rawValue   = prop.GetValue(value);
                var stringVal  = rawValue?.ToString();
                var maskedVal  = TryMaskField(prop.Name, stringVal);

                propName = prop.Name;
                logValue = maskedVal is not null
                    ? new ScalarValue(maskedVal)
                    : propertyValueFactory.CreatePropertyValue(rawValue, destructureObjects: false);
            }
            catch
            {
                // Property accessor threw — omit the property rather than crash
                propName = prop.Name;
                logValue = new ScalarValue("[ERROR_READING]");
            }

            logProps.Add(new LogEventProperty(propName, logValue));
        }

        result = new StructureValue(logProps, typeTag: type.Name);
        return true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string? TryMaskField(string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // 1 — Field-name match
        if (_piiFieldNames.Contains(propertyName.ToLowerInvariant()))
        {
            return RedactByFieldName(propertyName, value);
        }

        // 2 — Pattern detection on plain string values
        if (PiiMaskingPatterns.EmailRegex.IsMatch(value)) return PiiMaskingPatterns.MaskEmail(value);
        if (PiiMaskingPatterns.SsnRegex.IsMatch(value))   return PiiMaskingPatterns.MaskSsn(value);
        if (PiiMaskingPatterns.PhoneRegex.IsMatch(value)) return PiiMaskingPatterns.MaskPhone(value);

        return null;
    }

    private static string RedactByFieldName(string fieldName, string value)
    {
        var lc = fieldName.ToLowerInvariant();

        if (lc is "email")                                        return PiiMaskingPatterns.MaskEmail(value);
        if (lc is "phonenumber" or "phone")                      return PiiMaskingPatterns.MaskPhone(value);
        if (lc is "ssn" or "socialsecuritynumber")               return PiiMaskingPatterns.MaskSsn(value);
        if (lc is "dateofbirth" or "dob" or "birthdate")         return PiiMaskingPatterns.MaskDateOfBirth(value);
        if (lc is "firstname" or "lastname"
                or "patientname" or "fullname" or "name")        return PiiMaskingPatterns.MaskName(value);

        return PiiMaskingPatterns.MaskGeneric(value);
    }
}
