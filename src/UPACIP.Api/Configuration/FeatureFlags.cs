namespace UPACIP.Api.Configuration;

/// <summary>
/// String constants that map to keys in the <c>FeatureManagement</c> section of
/// <c>appsettings.json</c>.  Use these instead of inline magic strings so that
/// rename-refactors remain compile-safe and typos are caught by the compiler.
///
/// Usage examples:
/// <code>
/// // Controller action gate
/// [FeatureGate(FeatureFlags.AiDocumentParsing)]
/// public IActionResult ParseDocument() { … }
///
/// // Programmatic check
/// bool enabled = await _featureManager.IsEnabledAsync(FeatureFlags.SmsNotifications);
/// </code>
/// </summary>
public static class FeatureFlags
{
    /// <summary>AI-powered document parsing pipeline (Phase 2).</summary>
    public const string AiDocumentParsing = "AiDocumentParsing";

    /// <summary>SMS appointment reminders via Twilio (Phase 2).</summary>
    public const string SmsNotifications = "SmsNotifications";

    /// <summary>Natural-language conversational intake flow (Phase 3).</summary>
    public const string ConversationalIntake = "ConversationalIntake";

    /// <summary>Automated waitlist management and slot backfill (Phase 1 GA).</summary>
    public const string WaitlistManagement = "WaitlistManagement";
}
