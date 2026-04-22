namespace UPACIP.Service.Documents;

/// <summary>
/// Thrown when a clinical document upload fails server-side validation (US_038 AC-5).
///
/// The <see cref="Message"/> is safe to surface directly to the caller because it
/// contains only supported-format guidance and size limits — no internal paths or keys.
/// </summary>
public sealed class DocumentValidationException : Exception
{
    public DocumentValidationException(string message) : base(message) { }
}
