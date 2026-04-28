using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.AiSafety.Models;

namespace UPACIP.Service.AiSafety;

/// <summary>
/// Pattern-based prompt injection detector with medical context scoring
/// (US_079 task_001, AIR-S06, AIR-S04).
///
/// <para>
/// Patterns are loaded from <c>config/prompt-injection-patterns.json</c> via
/// <see cref="IOptionsMonitor{TOptions}"/> and hot-reloaded when the file changes —
/// the compiled regex cache is rebuilt automatically on each change notification.
/// Each regex executes with a 100 ms timeout to prevent ReDoS attacks
/// (OWASP LLM01, NIST AI 100-2,
/// https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-regex).
/// </para>
///
/// <para>
/// When a pattern match is found the surrounding ±50-character context window is
/// checked for clinical terminology (<see cref="MedicalTermAllowlist"/>, ICD-10
/// codes, and clinical keyword indicators).  If the medical context score reaches
/// or exceeds 0.7 the match is classified as a false positive, suppressed, and
/// logged at Information level for operator review (US_079 edge case).
/// </para>
///
/// <para>Singleton lifetime — thread-safe via <c>volatile</c> cache swap.</para>
/// </summary>
public sealed class PromptInjectionDetector : IPromptInjectionDetector, IDisposable
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private const int   ContextWindowRadius      = 50;
    private const float MedicalContextThreshold  = 0.7f;
    private const float SeverityWeightCritical   = 1.0f;
    private const float SeverityWeightHigh       = 0.8f;
    private const float SeverityWeightMedium     = 0.5f;
    private const float SeverityWeightLow        = 0.2f;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    // ─────────────────────────────────────────────────────────────────────────
    // Static lookup tables (severity → weight / sort order)
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, float> SeverityWeights =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { nameof(InjectionSeverity.Critical), SeverityWeightCritical },
            { nameof(InjectionSeverity.High),     SeverityWeightHigh     },
            { nameof(InjectionSeverity.Medium),   SeverityWeightMedium   },
            { nameof(InjectionSeverity.Low),      SeverityWeightLow      },
        };

    private static readonly Dictionary<string, int> SeverityOrder =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { nameof(InjectionSeverity.Critical), 0 },
            { nameof(InjectionSeverity.High),     1 },
            { nameof(InjectionSeverity.Medium),   2 },
            { nameof(InjectionSeverity.Low),      3 },
        };

    // ─────────────────────────────────────────────────────────────────────────
    // Static compiled regexes reused on every call
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Strips non-printable ASCII control characters (0x00–0x08, 0x0B, 0x0C, 0x0E–0x1F)
    /// while preserving LF (0x0A), CR (0x0D), and TAB (0x09).
    /// </summary>
    private static readonly Regex ControlCharRegex = new(
        @"[\x00-\x08\x0B\x0C\x0E-\x1F]",
        RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>Detects ICD-10 codes (e.g., J18.9, E11.65) in the clinical context window.</summary>
    private static readonly Regex Icd10Regex = new(
        @"[A-Z]\d{2}\.\d+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // ─────────────────────────────────────────────────────────────────────────
    // Clinical keyword indicators used by the false-positive heuristic
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly string[] ClinicalKeywords =
    [
        "dosage", "medication", "symptoms", "diagnosis", "clinical",
        "patient", "treatment", "prescription", "allergy", "contraindication",
    ];

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ILogger<PromptInjectionDetector>        _logger;
    private readonly IDisposable?                            _changeSubscription;

    // Thread-safe via volatile reference swap: readers always see a consistent snapshot.
    private volatile CompiledPatternCache _cache;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public PromptInjectionDetector(
        IOptionsMonitor<List<InjectionPattern>> patternsMonitor,
        ILogger<PromptInjectionDetector>        logger)
    {
        _logger = logger;

        _cache = BuildCache(patternsMonitor.CurrentValue);

        _changeSubscription = patternsMonitor.OnChange(newPatterns =>
        {
            _cache = BuildCache(newPatterns);
            _logger.LogInformation(
                "PromptInjectionDetector: pattern cache reloaded. PatternCount={Count}",
                newPatterns?.Count ?? 0);
        });

        _logger.LogInformation(
            "PromptInjectionDetector: initialised with {Count} patterns.",
            _cache.Entries.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IPromptInjectionDetector
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<InjectionDetectionResult> DetectAsync(
        string            userInput,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return Task.FromResult(new InjectionDetectionResult
            {
                IsInjectionDetected = false,
                SanitizedText       = userInput ?? string.Empty,
            });
        }

        var entries          = _cache.Entries;
        var detectedPatterns = new List<DetectedPattern>();
        var matchIntervals   = new List<(int start, int end)>();
        bool falsePositive   = false;

        foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            MatchCollection matches;
            try
            {
                matches = entry.Regex.Matches(userInput);
            }
            catch (RegexMatchTimeoutException ex)
            {
                _logger.LogWarning(ex,
                    "PromptInjectionDetector: regex timeout — pattern skipped. Category={Category}",
                    entry.Pattern.Category);
                continue;
            }

            foreach (Match match in matches)
            {
                if (IsMedicalFalsePositive(userInput, match))
                {
                    falsePositive = true;
                    _logger.LogInformation(
                        "PromptInjectionDetector: medical false positive suppressed. " +
                        "Category={Category} MatchPosition={Position}",
                        entry.Pattern.Category, match.Index);
                    continue;
                }

                // Truncate matched text to 50 chars for safe audit storage (AIR-S04).
                var matchedText = match.Value.Length > 50
                    ? string.Concat(match.Value.AsSpan(0, 50), "...")
                    : match.Value;

                detectedPatterns.Add(new DetectedPattern
                {
                    Category      = entry.Pattern.Category,
                    MatchedText   = matchedText,
                    MatchPosition = match.Index,
                    Severity      = entry.Pattern.Severity,
                    Description   = entry.Pattern.Description,
                });

                matchIntervals.Add((match.Index, match.Index + match.Length));
            }
        }

        // Build sanitised text: replace matched regions, strip control chars, NFC-normalise.
        string sanitized = BuildSanitizedText(userInput, matchIntervals);
        sanitized = StripControlCharsAndNormalize(sanitized);

        float riskScore = detectedPatterns.Count > 0
            ? detectedPatterns.Max(p => SeverityWeights.GetValueOrDefault(p.Severity, SeverityWeightLow))
            : 0.0f;

        return Task.FromResult(new InjectionDetectionResult
        {
            IsInjectionDetected     = detectedPatterns.Count > 0,
            DetectedPatterns        = detectedPatterns.AsReadOnly(),
            SanitizedText           = sanitized,
            RiskScore               = riskScore,
            WasMedicalFalsePositive = falsePositive,
        });
    }

    /// <inheritdoc/>
    public async Task<InjectionDetectionResult> SanitizeAsync(
        string            userInput,
        string            userId,
        CancellationToken cancellationToken = default)
    {
        var result = await DetectAsync(userInput, cancellationToken);

        if (result.IsInjectionDetected)
        {
            // Log one audit event per detected pattern — sanitised input only, never raw payload.
            foreach (var detected in result.DetectedPatterns)
            {
                _logger.LogWarning(
                    "PromptInjectionDetector: injection detected and sanitised. " +
                    "UserId={UserId} Category={Category} Severity={Severity} " +
                    "RiskScore={RiskScore} MatchPosition={Position}",
                    userId,
                    detected.Category,
                    detected.Severity,
                    result.RiskScore,
                    detected.MatchPosition);
            }
        }

        if (result.WasMedicalFalsePositive && !result.IsInjectionDetected)
        {
            _logger.LogInformation(
                "PromptInjectionDetector: all matches suppressed as medical false positives. " +
                "UserId={UserId}",
                userId);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IDisposable
    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose() => _changeSubscription?.Dispose();

    // ─────────────────────────────────────────────────────────────────────────
    // Private — cache building
    // ─────────────────────────────────────────────────────────────────────────

    private CompiledPatternCache BuildCache(List<InjectionPattern>? patterns)
    {
        if (patterns is null || patterns.Count == 0)
        {
            _logger.LogWarning(
                "PromptInjectionDetector: no patterns configured. " +
                "Ensure PromptInjectionPatterns section is present in configuration.");
            return new CompiledPatternCache([]);
        }

        var entries = new List<PatternEntry>(patterns.Count);

        foreach (var pattern in patterns.OrderBy(p =>
            SeverityOrder.GetValueOrDefault(p.Severity, 99)))
        {
            if (string.IsNullOrWhiteSpace(pattern.RegexPattern))
                continue;

            try
            {
                var regex = new Regex(
                    pattern.RegexPattern,
                    RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline,
                    RegexTimeout);

                entries.Add(new PatternEntry(pattern, regex));
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex,
                    "PromptInjectionDetector: invalid regex skipped. " +
                    "Category={Category} Pattern={Pattern}",
                    pattern.Category, pattern.RegexPattern);
            }
        }

        return new CompiledPatternCache(entries);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private — medical context scoring
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when the ±<see cref="ContextWindowRadius"/>-character window
    /// around <paramref name="match"/> in <paramref name="input"/> indicates a legitimate
    /// clinical phrase rather than a prompt injection attempt (US_079 edge case).
    /// </summary>
    private static bool IsMedicalFalsePositive(string input, Match match)
    {
        int    windowStart = Math.Max(0, match.Index - ContextWindowRadius);
        int    windowEnd   = Math.Min(input.Length, match.Index + match.Length + ContextWindowRadius);
        string window      = input[windowStart..windowEnd];

        float score = 0.0f;

        // 1. Medical eponyms / disease-name terms from the allowlist.
        if (window.Split([' ', ',', '.', ';', ':', '\n', '\r', '\t', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Any(word => MedicalTermAllowlist.Contains(word.Trim('"', '\''))))
        {
            score += 0.4f;
        }

        // 2. ICD-10 code pattern in the context window.
        try
        {
            if (Icd10Regex.IsMatch(window))
                score += 0.5f;
        }
        catch (RegexMatchTimeoutException) { /* Don't suppress on timeout. */ }

        // 3. Clinical keyword indicators.
        if (ClinicalKeywords.Any(keyword => window.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.3f;
        }

        return score >= MedicalContextThreshold;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private — text sanitisation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces all non-overlapping match intervals in <paramref name="original"/>
    /// with <c>[SANITIZED]</c> and returns the result.
    /// </summary>
    private static string BuildSanitizedText(
        string                      original,
        List<(int start, int end)>  intervals)
    {
        if (intervals.Count == 0)
            return original;

        // Merge overlapping / adjacent intervals.
        var sorted = intervals.OrderBy(i => i.start).ToList();
        var merged = new List<(int start, int end)>(sorted.Count) { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            var (prevStart, prevEnd) = merged[^1];
            var (currStart, currEnd) = sorted[i];

            if (currStart <= prevEnd)
                merged[^1] = (prevStart, Math.Max(prevEnd, currEnd)); // Overlap: extend.
            else
                merged.Add((currStart, currEnd));
        }

        var sb  = new StringBuilder(original.Length);
        int pos = 0;

        foreach (var (start, end) in merged)
        {
            if (start > pos)
                sb.Append(original, pos, start - pos);

            sb.Append("[SANITIZED]");
            pos = end;
        }

        if (pos < original.Length)
            sb.Append(original, pos, original.Length - pos);

        return sb.ToString();
    }

    /// <summary>
    /// Strips ASCII control characters (preserving LF, CR, TAB) and normalises
    /// to Unicode NFC form to prevent homoglyph-based injection attacks.
    /// </summary>
    private static string StripControlCharsAndNormalize(string text)
    {
        string stripped;
        try
        {
            stripped = ControlCharRegex.Replace(text, string.Empty);
        }
        catch (RegexMatchTimeoutException)
        {
            stripped = text; // Timeout: leave control chars in place rather than blocking.
        }

        return stripped.Normalize(NormalizationForm.FormC);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private record types (immutable cache snapshot)
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record PatternEntry(InjectionPattern Pattern, Regex Regex);

    private sealed record CompiledPatternCache(IReadOnlyList<PatternEntry> Entries);
}
