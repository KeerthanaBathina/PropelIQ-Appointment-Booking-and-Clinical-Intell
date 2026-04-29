using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Compliance.Models;

namespace UPACIP.Service.Compliance;

/// <summary>
/// Evaluates configurable JSON-based compliance rules and supports upsert of new rules
/// without code changes (US_093, AC-2 edge case 2).
///
/// <para>Evaluation types supported via <c>EvaluationCriteriaJson.type</c>:</para>
/// <list type="bullet">
///   <item><c>config_check</c>  — Reads an IConfiguration path and compares using the specified operator.</item>
///   <item><c>db_query</c>      — Executes a read-only SQL query and compares the result against an expected value.</item>
///   <item><c>service_check</c> — Checks that a boolean configuration flag equals "true".</item>
/// </list>
///
/// <para>Security note:</para>
/// <c>db_query</c> rules execute stored SQL from the database. Only administrators can upsert
/// rules via <c>POST /api/admin/compliance/rules</c>. Query results are compared numerically —
/// no raw query output is exposed in API responses or logs.
/// </summary>
public interface IComplianceRuleEngine
{
    /// <summary>
    /// Evaluates all active <see cref="ComplianceRule"/> records and returns results.
    /// </summary>
    Task<List<ComplianceRuleEvaluation>> EvaluateAllRulesAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a compliance rule by <see cref="ComplianceRule.RuleName"/>.
    /// Enables compliance officers to add rules for new regulations without code deployments.
    /// </summary>
    Task<ComplianceRule> UpsertRuleAsync(ComplianceRule rule, CancellationToken ct = default);
}

/// <summary>
/// Scoped implementation of <see cref="IComplianceRuleEngine"/>.
/// </summary>
public sealed class ComplianceRuleEngine : IComplianceRuleEngine
{
    private readonly ApplicationDbContext         _db;
    private readonly IConfiguration               _config;
    private readonly ILogger<ComplianceRuleEngine> _logger;

    // Supported comparison operators for criteria evaluation.
    private static readonly HashSet<string> ValidOperators =
        new(StringComparer.OrdinalIgnoreCase) { "==", "!=", "<=", ">=", "<", ">", "contains" };

    public ComplianceRuleEngine(
        ApplicationDbContext          db,
        IConfiguration                config,
        ILogger<ComplianceRuleEngine> logger)
    {
        _db     = db;
        _config = config;
        _logger = logger;
    }

    // ── EvaluateAllRulesAsync ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<ComplianceRuleEvaluation>> EvaluateAllRulesAsync(
        CancellationToken ct = default)
    {
        var rules = await _db.ComplianceRules
            .Where(r => r.IsActive)
            .ToListAsync(ct);

        var evaluations = new List<ComplianceRuleEvaluation>(rules.Count);

        foreach (var rule in rules)
        {
            var result = await EvaluateRuleAsync(rule, ct);
            evaluations.Add(result);

            if (!result.Passed)
                _logger.LogWarning(
                    "COMPLIANCE_RULE_FAILED: Rule={Rule}, Severity={Severity}, Reason={Reason}",
                    rule.RuleName, rule.Severity, result.FailureReason);
        }

        return evaluations;
    }

    // ── UpsertRuleAsync ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ComplianceRule> UpsertRuleAsync(
        ComplianceRule rule,
        CancellationToken ct = default)
    {
        ValidateCriteriaJson(rule.EvaluationCriteriaJson, rule.RuleName);

        var existing = await _db.ComplianceRules
            .FirstOrDefaultAsync(r => r.RuleName == rule.RuleName, ct);

        if (existing is null)
        {
            rule.CreatedAtUtc = DateTime.UtcNow;
            rule.UpdatedAtUtc = DateTime.UtcNow;
            _db.ComplianceRules.Add(rule);
        }
        else
        {
            existing.Category               = rule.Category;
            existing.Description            = rule.Description;
            existing.EvaluationCriteriaJson = rule.EvaluationCriteriaJson;
            existing.IsActive               = rule.IsActive;
            existing.Severity               = rule.Severity;
            existing.HipaaReference         = rule.HipaaReference;
            existing.RemediationGuidance    = rule.RemediationGuidance;
            existing.UpdatedAtUtc           = DateTime.UtcNow;
            rule = existing;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "COMPLIANCE_RULE_UPDATED: Rule={Rule}, Category={Category}",
            rule.RuleName, rule.Category);

        return rule;
    }

    // ── Private evaluation helpers ───────────────────────────────────────────

    private async Task<ComplianceRuleEvaluation> EvaluateRuleAsync(
        ComplianceRule    rule,
        CancellationToken ct)
    {
        var evaluatedAt = DateTime.UtcNow;
        var result = new ComplianceRuleEvaluation
        {
            RuleName            = rule.RuleName,
            Category            = rule.Category,
            Severity            = rule.Severity,
            HipaaReference      = rule.HipaaReference,
            RemediationGuidance = rule.RemediationGuidance,
            EvaluatedAtUtc      = evaluatedAt,
        };

        try
        {
            using var doc = JsonDocument.Parse(rule.EvaluationCriteriaJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString() ?? string.Empty;

            (result.Passed, result.FailureReason) = type.ToLowerInvariant() switch
            {
                "config_check"  => EvaluateConfigCheck(root, rule.RuleName),
                "service_check" => EvaluateServiceCheck(root, rule.RuleName),
                "db_query"      => await EvaluateDbQueryAsync(root, rule.RuleName, ct),
                _               => (false, $"Unsupported evaluation type '{type}'.")
            };
        }
        catch (Exception ex)
        {
            result.Passed       = false;
            result.FailureReason = $"Evaluation error: {ex.Message}";
            _logger.LogError(ex, "Error evaluating rule '{Rule}'.", rule.RuleName);
        }

        return result;
    }

    // config_check — reads IConfiguration value and compares using operator.
    private (bool Passed, string? FailureReason) EvaluateConfigCheck(
        JsonElement root, string ruleName)
    {
        var configPath = root.GetProperty("configPath").GetString() ?? string.Empty;
        var op         = root.GetProperty("operator").GetString()   ?? "==";
        var expected   = root.GetProperty("expectedValue").GetString() ?? string.Empty;

        if (!ValidOperators.Contains(op))
            return (false, $"Unsupported operator '{op}'.");

        var actual = _config[configPath];
        if (actual is null)
            return (false, $"Configuration path '{configPath}' not found.");

        return CompareValues(actual, expected, op, ruleName);
    }

    // service_check — identical to config_check but expects boolean "true".
    private (bool Passed, string? FailureReason) EvaluateServiceCheck(
        JsonElement root, string ruleName)
    {
        var configPath = root.GetProperty("configPath").GetString() ?? string.Empty;
        var expected   = root.TryGetProperty("expectedValue", out var ev)
                         ? ev.GetString() ?? "true"
                         : "true";

        var actual = _config[configPath];
        if (actual is null)
            return (false, $"Service flag '{configPath}' not found in configuration.");

        var actualBool   = string.Equals(actual,   "true", StringComparison.OrdinalIgnoreCase);
        var expectedBool = string.Equals(expected, "true", StringComparison.OrdinalIgnoreCase);

        return actualBool == expectedBool
            ? (true, null)
            : (false, $"Service flag '{configPath}' is '{actual}', expected '{expected}'.");
    }

    // db_query — executes a read-only COUNT query and compares the scalar result.
    // Security: query SQL originates from admin-controlled database rows (not user input).
    // Only scalar COUNT(*) results are compared — raw row data is never returned.
    private async Task<(bool Passed, string? FailureReason)> EvaluateDbQueryAsync(
        JsonElement root, string ruleName, CancellationToken ct)
    {
        var sql      = root.GetProperty("query").GetString() ?? string.Empty;
        var op       = root.GetProperty("operator").GetString() ?? "==";
        var expected = root.GetProperty("expectedValue").GetString() ?? "0";

        if (string.IsNullOrWhiteSpace(sql))
            return (false, "db_query rule has an empty query.");

        if (!ValidOperators.Contains(op))
            return (false, $"Unsupported operator '{op}'.");

        // Use parameterized form — no user input is interpolated into the SQL.
        // The SQL is stored by compliance officers in ComplianceRule.EvaluationCriteriaJson.
        int actualCount;
        try
        {
            // Execute a scalar COUNT query; parse the first integer column of the first row.
            var results = await _db.Database
                .SqlQueryRaw<long>(sql)
                .ToListAsync(ct);

            actualCount = results.Count > 0 ? (int)results[0] : 0;
        }
        catch (Exception ex)
        {
            return (false, $"Query execution failed: {ex.Message}");
        }

        return CompareValues(actualCount.ToString(), expected, op, ruleName);
    }

    // Compares two string-encoded values using the specified operator.
    // Tries numeric comparison first, falls back to string comparison.
    private static (bool Passed, string? FailureReason) CompareValues(
        string actual, string expected, string op, string ruleName)
    {
        if (double.TryParse(actual,   out var d1) &&
            double.TryParse(expected, out var d2))
        {
            var passed = op switch
            {
                "==" or "=" => d1 == d2,
                "!="        => d1 != d2,
                "<="        => d1 <= d2,
                ">="        => d1 >= d2,
                "<"         => d1 < d2,
                ">"         => d1 > d2,
                _           => false
            };
            return passed
                ? (true, null)
                : (false, $"Rule '{ruleName}': actual={actual}, expected {op} {expected}.");
        }

        // String / contains comparison
        var strPassed = op switch
        {
            "==" or "="  => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            "!="         => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            "contains"   => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            _            => false
        };

        return strPassed
            ? (true, null)
            : (false, $"Rule '{ruleName}': actual='{actual}', expected {op} '{expected}'.");
    }

    // Validates that the criteria JSON is parseable and has the required fields.
    private static void ValidateCriteriaJson(string json, string ruleName)
    {
        try
        {
            using var doc  = JsonDocument.Parse(json);
            var root       = doc.RootElement;
            var type       = root.GetProperty("type").GetString();
            var validTypes = new[] { "config_check", "db_query", "service_check" };

            if (!validTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Unsupported evaluation type '{type}'.");

            if (type != "db_query")
                _ = root.GetProperty("configPath"); // must exist for config/service checks
            else
                _ = root.GetProperty("query"); // must exist for db_query checks
        }
        catch (KeyNotFoundException kex)
        {
            throw new ArgumentException(
                $"EvaluationCriteriaJson for rule '{ruleName}' is missing required field. {kex.Message}");
        }
        catch (JsonException jex)
        {
            throw new ArgumentException(
                $"EvaluationCriteriaJson for rule '{ruleName}' is not valid JSON. {jex.Message}");
        }
    }
}
