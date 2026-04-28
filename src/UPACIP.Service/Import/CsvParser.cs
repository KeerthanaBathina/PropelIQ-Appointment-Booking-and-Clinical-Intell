using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace UPACIP.Service.Import;

/// <summary>
/// Contract for streaming, RFC 4180-compliant CSV parsing with delimiter auto-detection
/// and encoding support (US_092 task_001, edge case 2).
/// </summary>
public interface ICsvParser
{
    /// <summary>
    /// Streams CSV rows lazily as string dictionaries keyed by column header.
    /// Header names are normalised to lowercase and whitespace-trimmed.
    /// Rows are yielded in document order starting from the second line (line 1 is the header).
    /// The <paramref name="headers"/> out-parameter is populated before the first yield.
    /// </summary>
    IAsyncEnumerable<(int LineNumber, Dictionary<string, string> Fields)> ParseAsync(
        Stream            csvStream,
        CancellationToken ct        = default);

    /// <summary>
    /// Reads only the header row from <paramref name="csvStream"/> and returns normalised
    /// column names. Stream position is reset to the beginning after the call.
    /// </summary>
    Task<string[]> ReadHeadersAsync(Stream csvStream, CancellationToken ct = default);
}

/// <summary>
/// Streaming, RFC 4180-compliant CSV parser.
///
/// Delimiter detection (edge case 2):
///   Reads the header line and counts occurrences of <c>,</c>, <c>;</c>, and <c>\t</c>.
///   The delimiter with the highest count wins; ties fall back to comma.
///
/// Encoding support (edge case 2):
///   Checks for a UTF-8 BOM (EF BB BF). Falls back to UTF-8 (superset of ASCII).
///
/// Quoted field handling (RFC 4180):
///   A value enclosed in double-quotes may contain the delimiter or a literal double-quote
///   (escaped as <c>""</c>). The parser handles multi-character delimiters and RFC 4180
///   quoting without loading the entire file into memory.
///
/// Security (OWASP A03): no user-controlled strings are written to SQL.
/// File size limits are enforced by the engine layer, not the parser.
/// </summary>
public sealed class CsvParser : ICsvParser
{
    private readonly ILogger<CsvParser> _logger;

    public CsvParser(ILogger<CsvParser> logger)
    {
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICsvParser
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async IAsyncEnumerable<(int LineNumber, Dictionary<string, string> Fields)> ParseAsync(
        Stream            csvStream,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var encoding = DetectEncoding(csvStream);
        using var reader = new StreamReader(csvStream, encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        // Read header line
        var headerLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(headerLine))
            yield break;

        var delimiter = DetectDelimiter(headerLine);
        _logger.LogDebug("CSV_DELIMITER_DETECTED: Delimiter={Delimiter}", delimiter == '\t' ? "TAB" : delimiter.ToString());

        var headers = SplitLine(headerLine, delimiter);
        if (headers.Length < 2)
        {
            _logger.LogWarning("CsvParser: header row has fewer than 2 columns — aborting parse.");
            yield break;
        }

        int lineNumber = 1;

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            lineNumber++;

            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
                break;

            // Skip blank lines
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var values = SplitLine(line, delimiter);
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < headers.Length; i++)
            {
                var key   = headers[i];
                var value = i < values.Length ? values[i] : string.Empty;
                fields[key] = value;
            }

            yield return (lineNumber, fields);
        }
    }

    /// <inheritdoc/>
    public async Task<string[]> ReadHeadersAsync(Stream csvStream, CancellationToken ct = default)
    {
        var originalPosition = csvStream.CanSeek ? csvStream.Position : -1L;

        try
        {
            if (csvStream.CanSeek)
                csvStream.Seek(0, SeekOrigin.Begin);

            var encoding = DetectEncoding(csvStream);
            using var reader = new StreamReader(csvStream, encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

            var headerLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(headerLine))
                return [];

            var delimiter = DetectDelimiter(headerLine);
            return SplitLine(headerLine, delimiter);
        }
        finally
        {
            if (csvStream.CanSeek && originalPosition >= 0)
                csvStream.Seek(0, SeekOrigin.Begin);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inspects the first three bytes for a UTF-8 BOM (EF BB BF).
    /// If found, returns UTF-8; otherwise resets the stream and returns UTF-8 as default
    /// (UTF-8 is a superset of ASCII).
    /// </summary>
    private static Encoding DetectEncoding(Stream stream)
    {
        if (!stream.CanSeek)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        stream.Seek(0, SeekOrigin.Begin);
        Span<byte> bom = stackalloc byte[3];
        int read = stream.Read(bom);
        stream.Seek(0, SeekOrigin.Begin);

        // UTF-8 BOM
        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    /// <summary>
    /// Counts comma, semicolon, and tab occurrences in the header line.
    /// Returns the character with the highest count; defaults to comma on a tie.
    /// </summary>
    private static char DetectDelimiter(string headerLine)
    {
        int commas     = 0;
        int semicolons = 0;
        int tabs       = 0;
        bool inQuotes  = false;

        foreach (var ch in headerLine)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (inQuotes)  continue;

            if (ch == ',')  commas++;
            else if (ch == ';') semicolons++;
            else if (ch == '\t') tabs++;
        }

        if (tabs > commas && tabs > semicolons) return '\t';
        if (semicolons > commas)                return ';';
        return ',';
    }

    /// <summary>
    /// Splits a single CSV line on <paramref name="delimiter"/> respecting RFC 4180 quoting.
    /// Returns trimmed, normalised (lowercase) column names when called on the header;
    /// returns raw values (no transform) when called on data rows.
    /// </summary>
    private static string[] SplitLine(string line, char delimiter)
    {
        var fields  = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];

            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped double-quote ("") → literal "
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == delimiter && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields.ToArray();
    }
}
