using System.Text;
using System.Text.RegularExpressions;

namespace UPACIP.Service.Rag.Chunking;

/// <summary>
/// Static helpers that sanitise raw document text before BPE tokenisation
/// (US_076 edge cases: table-to-text conversion, image exclusion, whitespace normalisation).
///
/// All methods are pure / side-effect-free.  Call order in the pipeline:
///   1. <see cref="ReplaceImages"/>
///   2. <see cref="ConvertTablesToText"/>
///   3. <see cref="NormalizeWhitespace"/>
/// </summary>
internal static class TextPreprocessor
{
    // ── Compiled regexes (compiled once at type initialisation) ──────────────

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Matches HTML &lt;img&gt; tags (both self-closing and non-self-closing).</summary>
    private static readonly Regex HtmlImageRegex = new(
        @"<img\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>Matches Markdown image syntax: ![alt text](url "optional title").</summary>
    private static readonly Regex MarkdownImageRegex = new(
        @"!\[[^\]]*\]\([^)]*\)",
        RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>Matches an entire HTML &lt;table&gt;…&lt;/table&gt; block.</summary>
    private static readonly Regex HtmlTableRegex = new(
        @"<table\b[^>]*>.*?</table>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>Matches a single HTML &lt;tr&gt;…&lt;/tr&gt; row.</summary>
    private static readonly Regex HtmlRowRegex = new(
        @"<tr\b[^>]*>(.*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>Matches HTML &lt;th&gt; or &lt;td&gt; cell content.</summary>
    private static readonly Regex HtmlCellRegex = new(
        @"<t[hd]\b[^>]*>(.*?)</t[hd]>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>Matches any remaining HTML tag (stripped during cell text extraction).</summary>
    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>
    /// Matches Markdown pipe-table rows:
    ///   | col1 | col2 | …  (separator rows with only dashes/pipes are excluded).
    /// </summary>
    private static readonly Regex MarkdownTableRowRegex = new(
        @"^\s*\|(.+)\|\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>Markdown table separator row (---): skipped during conversion.</summary>
    private static readonly Regex MarkdownSeparatorRowRegex = new(
        @"^\s*\|[\s\-|]+\|\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>Two or more consecutive whitespace characters (including newlines/tabs).</summary>
    private static readonly Regex MultiWhitespaceRegex = new(
        @"[\s]{2,}",
        RegexOptions.Compiled,
        RegexTimeout);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces HTML &lt;img&gt; tags and Markdown image syntax with the
    /// <c>[image-content-excluded]</c> placeholder (US_076 edge case: images skipped).
    /// </summary>
    public static string ReplaceImages(string text)
    {
        const string placeholder = "[image-content-excluded]";
        text = HtmlImageRegex.Replace(text, placeholder);
        text = MarkdownImageRegex.Replace(text, placeholder);
        return text;
    }

    /// <summary>
    /// Converts HTML and Markdown tables to "Column1: Value1 | Column2: Value2" structured
    /// text rows (US_076 edge case: tables converted to structured text).
    ///
    /// Each row is emitted as a single line; table structure is preserved as key-value pairs
    /// using the first row as column headers when available.
    /// </summary>
    public static string ConvertTablesToText(string text)
    {
        // ── HTML tables ────────────────────────────────────────────────────
        text = HtmlTableRegex.Replace(text, m => ConvertHtmlTableMatch(m.Value));

        // ── Markdown pipe tables ───────────────────────────────────────────
        text = ConvertMarkdownTables(text);

        return text;
    }

    /// <summary>
    /// Normalises whitespace: collapses runs of whitespace/newlines to a single space,
    /// trims leading/trailing whitespace, and applies Unicode NFC normalisation.
    /// </summary>
    public static string NormalizeWhitespace(string text)
    {
        // Unicode NFC normalisation first (canonical decomposition + canonical composition)
        text = text.Normalize(NormalizationForm.FormC);

        // Replace all multi-whitespace (including \n, \r, \t) with a single space
        text = MultiWhitespaceRegex.Replace(text, " ");

        return text.Trim();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string ConvertHtmlTableMatch(string tableHtml)
    {
        var sb = new StringBuilder();

        var rows = HtmlRowRegex.Matches(tableHtml);
        string[] headers = [];

        foreach (Match row in rows)
        {
            var cells = HtmlCellRegex.Matches(row.Groups[1].Value);
            var cellTexts = cells
                .Select(c => HtmlTagRegex.Replace(c.Groups[1].Value, " ").Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToArray();

            if (cellTexts.Length == 0) continue;

            if (headers.Length == 0)
            {
                // Treat first row as headers
                headers = cellTexts;
                continue;
            }

            // Emit "Header: Value | Header: Value …"
            var parts = cellTexts
                .Select((v, i) => i < headers.Length
                    ? $"{headers[i]}: {v}"
                    : v)
                .ToArray();

            sb.AppendLine(string.Join(" | ", parts));
        }

        return sb.Length > 0 ? sb.ToString() : string.Empty;
    }

    private static string ConvertMarkdownTables(string text)
    {
        // Find groups of consecutive pipe-table lines
        var lines   = text.Split('\n');
        var result  = new StringBuilder();
        var inTable = false;
        var headers = Array.Empty<string>();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');

            if (MarkdownSeparatorRowRegex.IsMatch(line))
            {
                // Separator row — skip but remain in table context
                inTable = true;
                continue;
            }

            if (MarkdownTableRowRegex.IsMatch(line))
            {
                var cells = line
                    .Split('|')
                    .Skip(1)
                    .SkipLast(1)
                    .Select(c => c.Trim())
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToArray();

                if (!inTable)
                {
                    // First data row becomes headers
                    headers  = cells;
                    inTable  = true;
                }
                else
                {
                    // Subsequent rows: emit as "Header: Value | …"
                    var parts = cells
                        .Select((v, i) => i < headers.Length
                            ? $"{headers[i]}: {v}"
                            : v)
                        .ToArray();

                    result.AppendLine(string.Join(" | ", parts));
                }

                continue;
            }

            // Non-table line — reset table state
            inTable = false;
            headers = Array.Empty<string>();
            result.AppendLine(line);
        }

        return result.ToString();
    }
}
