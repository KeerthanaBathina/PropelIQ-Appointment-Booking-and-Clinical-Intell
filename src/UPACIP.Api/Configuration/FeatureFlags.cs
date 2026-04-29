namespace UPACIP.Api.Configuration;

/// <summary>
/// String constants that map to keys in the <c>FeatureManagement</c> section of
/// <c>appsettings.json</c> (used by <c>Microsoft.FeatureManagement</c>) and to flag names
/// in <c>config/featureflags.json</c> (used by <c>IFeatureFlagService</c>).
///
/// Use these constants instead of inline magic strings so rename-refactors remain
/// compile-safe and typos are caught by the compiler.
///
/// Usage examples:
/// <code>
/// // Controller action gate (Microsoft.FeatureManagement)
/// [FeatureGate(FeatureFlags.AiDocumentParsing)]
/// public IActionResult ParseDocument() { … }
///
/// // Programmatic check via IFeatureFlagService (US_101)
/// if (_featureFlagService.IsEnabled(FeatureFlags.AiConversationalIntake)) { … }
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

    // ---- US_101 flags registered in config/featureflags.json ----

    /// <summary>AI-powered conversational patient intake flow (US_101).</summary>
    public const string AiConversationalIntake = "AiConversationalIntake";

    /// <summary>AI-assisted ICD-10/CPT medical code suggestion (US_101).</summary>
    public const string AiMedicalCoding = "AiMedicalCoding";

    /// <summary>Email appointment reminders (US_101).</summary>
    public const string EmailReminders = "EmailReminders";

    /// <summary>SMS appointment reminders (US_101).</summary>
    public const string SmsReminders = "SmsReminders";
}
