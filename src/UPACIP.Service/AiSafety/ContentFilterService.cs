using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.AiSafety.Models;

namespace UPACIP.Service.AiSafety;

/// <summary>
/// Rule definition for a single content filter category, loaded from
/// <c>config/content-filter-rules.json</c> (US_079 task_002, AIR-S05).
/// </summary>
public sealed class ContentFilterRule
{
    /// <summary>
    /// Filter category name — must match a <see cref="ContentFilterCategory"/> enum value.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Regular expression pattern used for compiled detection.</summary>
    public string RegexPattern { get; init; } = string.Empty;

    /// <summary>Human-readable description for audit logs.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Singleton content filter service that scans AI-generated responses for harmful,
/// discriminatory, or medically dangerous content using compiled regex patterns
/// (US_079 task_002, AIR-S05, AC-3).
///
/// <para>
/// Patterns are loaded from <c>config/content-filter-rules.json</c> via
/// <see cref="IOptionsMonitor{TOptions}"/> and hot-reloaded on file change.
/// Each regex executes with a 100 ms timeout (ReDoS protection, OWASP LLM02).
/// </para>
///
/// <para>
/// Blocked responses: <see cref="ContentFilterResult.OriginalResponseHash"/> stores the
/// SHA-256 hex hash for audit correlation — the harmful text is never logged or stored.
/// </para>
///
/// <para>Singleton lifetime — stateless apart from the volatile compiled regex cache.</para>
/// </summary>
public sealed class ContentFilterService : IContentFilterService, IDisposable
{
    private const string SafeFallbackMessage =
        "This response has been filtered for safety. Please consult your healthcare " +
        "provider directly for medical guidance. If you believe this is an error, " +
        "contact support.";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private readonly ILogger<ContentFilterService> _logger;
    private readonly IDisposable?                  _changeSubscription;

    // Thread-safe via volatile reference swap.
    private volatile CompiledRuleCache _cache;

    public ContentFilterService(
        IOptionsMonitor<List<ContentFilterRule>> rulesMonitor,
        ILogger<ContentFilterService>            logger)
    {
        _logger = logger;
        _cache  = BuildCache(rulesMonitor.CurrentValue);

        _changeSubscription = rulesMonitor.OnChange(newRules =>
        {
            _cache = BuildCache(newRules);
            _logger.LogInformation(
                "ContentFilterService: rule cache reloaded. RuleCount={Count}",
                newRules?.Count ?? 0);
        });

        _logger.LogInformation(
            "ContentFilterService: initialised with {Count} rules.",
            _cache.Entries.Count);
    }

    /// <inheritdoc/>
    public Task<ContentFilterResult> FilterResponseAsync(
        string            responseText,
        string            correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return Task.FromResult(new ContentFilterResult());

        var entries          = _cache.Entries;
        var blockedCategories = new List<ContentFilterCategory>();

        foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            bool matched;
            try
            {
                matched = entry.Regex.IsMatch(responseText);
            }
            catch (RegexMatchTimeoutException ex)
            {
                _logger.LogWarning(ex,
                    "ContentFilterService: regex timeout — rule skipped. Category={Category}",
                    entry.Rule.Category);
                continue;
            }

            if (!matched)
                continue;

            if (Enum.TryParse<ContentFilterCategory>(entry.Rule.Category, ignoreCase: true, out var category)
                && !blockedCategories.Contains(category))
            {
                blockedCategories.Add(category);
            }
        }

        if (blockedCategories.Count == 0)
            return Task.FromResult(new ContentFilterResult());

        var responseHash = ComputeSha256Hash(responseText);

        _logger.LogWarning(
            "ContentFilterService: response blocked. " +
            "CorrelationId={CorrelationId} Categories={Categories} ResponseHash={Hash}",
            correlationId,
            string.Join(", ", blockedCategories),
            responseHash);

        return Task.FromResult(new ContentFilterResult
        {
            IsBlocked            = true,
            BlockedCategories    = blockedCategories.AsReadOnly(),
            SafeResponse         = SafeFallbackMessage,
            OriginalResponseHash = responseHash,
        });
    }

    public void Dispose() => _changeSubscription?.Dispose();

    // ── Private helpers ───────────────────────────────────────────────────────

    private CompiledRuleCache BuildCache(List<ContentFilterRule>? rules)
    {
        if (rules is null || rules.Count == 0)
        {
            _logger.LogWarning(
                "ContentFilterService: no rules configured. " +
                "Ensure ContentFilterRules section is present in configuration.");
            return new CompiledRuleCache([]);
        }

        var entries = new List<RuleEntry>(rules.Count);

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.RegexPattern))
                continue;

            try
            {
                var regex = new Regex(
                    rule.RegexPattern,
                    RegexOptions.IgnoreCase | RegexOptions.Compiled,
                    RegexTimeout);

                entries.Add(new RuleEntry(rule, regex));
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex,
                    "ContentFilterService: invalid regex skipped. " +
                    "Category={Category} Pattern={Pattern}",
                    rule.Category, rule.RegexPattern);
            }
        }

        return new CompiledRuleCache(entries);
    }

    private static string ComputeSha256Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record RuleEntry(ContentFilterRule Rule, Regex Regex);

    private sealed record CompiledRuleCache(IReadOnlyList<RuleEntry> Entries);
}
