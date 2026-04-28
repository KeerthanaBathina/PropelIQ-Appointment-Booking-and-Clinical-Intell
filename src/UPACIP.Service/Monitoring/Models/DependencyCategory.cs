namespace UPACIP.Service.Monitoring.Models;

/// <summary>
/// Enumerates the top-level dependency categories tracked by the graceful-degradation
/// subsystem (US_083 task_002, AC-1, AC-2).
///
/// <list type="bullet">
///   <item><term>AiProviders</term><description>OpenAI / Anthropic Claude — drives AI intake, parsing, and coding features.</description></item>
///   <item><term>Redis</term><description>Cache / session store — drives slot caching and session management.</description></item>
///   <item><term>Database</term><description>PostgreSQL — drives core CRUD features.</description></item>
///   <item><term>ExternalServices</term><description>Third-party integrations (Twilio, SendGrid, etc.).</description></item>
/// </list>
/// </summary>
public enum DependencyCategory
{
    AiProviders,
    Redis,
    Database,
    ExternalServices,
}
