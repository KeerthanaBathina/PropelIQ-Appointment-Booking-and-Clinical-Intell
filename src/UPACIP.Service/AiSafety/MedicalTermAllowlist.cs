using System.Text.Json;

namespace UPACIP.Service.AiSafety;

/// <summary>
/// Static allowlist of medical eponyms and disease-name terms that resemble personal names
/// and must NOT be redacted by the PII name-detection heuristic (US_074 task_001, edge case).
///
/// <para>
/// Examples: "Addison" (Addison's disease), "Cushing" (Cushing's syndrome),
/// "Hodgkin" (Hodgkin's lymphoma).  Without this allowlist, the name-pattern regex would
/// incorrectly strip these clinical terms from AI prompts.
/// </para>
///
/// <para>
/// The built-in set is loaded at class initialisation.  An external JSON file at
/// <c>config/medical-term-allowlist.json</c> (relative to the application base directory)
/// is merged in when it exists, allowing operators to extend the list without redeployment.
/// </para>
/// </summary>
public static class MedicalTermAllowlist
{
    // ─────────────────────────────────────────────────────────────────────────
    // Built-in baseline — medical eponyms from major disease classifications.
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        "Addison", "Alzheimer", "Bartholin", "Bouchard", "Bright", "Broca", "Bruton",
        "Budd", "Chagas", "Chiari", "Conn", "Crohn", "Cushing", "Dandy", "DiGeorge",
        "Dupuytren", "Epstein", "Fallot", "Fanconi", "Felty", "Gaucher", "Gilbert",
        "Goodpasture", "Graves", "Hashimoto", "Henoch", "Hodgkin", "Horner", "Huntington",
        "Kaposi", "Kawasaki", "Klinefelter", "Lesch", "Lou", "Lyme", "Marfan", "McArdle",
        "Meniere", "Mobitz", "Niemann", "Noonan", "Osler", "Paget", "Parkinson", "Pick",
        "Plummer", "Raynaud", "Reiter", "Reye", "Riedel", "Rotor", "Sjogren", "Still",
        "Sturge", "Sweet", "Takayasu", "Tay", "Turner", "Virchow", "Waldenstrom", "Weber",
        "Wegener", "Werner", "Wilms", "Wilson", "Wolff",
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Merged set (built-in + any external additions loaded from file)
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> Terms;

    static MedicalTermAllowlist()
    {
        Terms = new HashSet<string>(BuiltIn, StringComparer.OrdinalIgnoreCase);
        TryLoadFromFile();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="term"/> is a recognised medical eponym
    /// or disease-name term that must not be redacted by the name-pattern heuristic.
    /// Comparison is case-insensitive.
    /// </summary>
    public static bool Contains(string term) => Terms.Contains(term);

    // ─────────────────────────────────────────────────────────────────────────
    // Private — file loading
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to load additional terms from <c>config/medical-term-allowlist.json</c>
    /// relative to the application base directory.  Silently ignores missing or malformed files.
    /// </summary>
    private static void TryLoadFromFile()
    {
        try
        {
            string configPath = Path.Combine(
                AppContext.BaseDirectory,
                "..",  // step up from /bin/Debug/net8.0/
                "..",
                "..",
                "..",
                "config",
                "medical-term-allowlist.json");

            string resolvedPath = Path.GetFullPath(configPath);

            if (!File.Exists(resolvedPath)) return;

            string json  = File.ReadAllText(resolvedPath);
            var    terms = JsonSerializer.Deserialize<string[]>(json);

            if (terms is null) return;

            foreach (string term in terms)
            {
                if (!string.IsNullOrWhiteSpace(term))
                    Terms.Add(term.Trim());
            }
        }
        catch
        {
            // Fail-open: built-in terms are always available.
        }
    }
}
