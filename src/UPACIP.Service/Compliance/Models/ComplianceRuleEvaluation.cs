namespace UPACIP.Service.Compliance.Models;

/// <summary>
/// Result of evaluating a single <c>ComplianceRule</c> (US_093, AC-2 edge case 2).
/// Returned by <c>IComplianceRuleEngine.EvaluateAllRulesAsync</c> for each active rule.
/// </summary>
public sealed class ComplianceRuleEvaluation
{
    /// <summary>Unique rule identifier (matches <c>ComplianceRule.RuleName</c>).</summary>
    public required string RuleName { get; set; }

    /// <summary>Whether the rule passed evaluation.</summary>
    public bool Passed { get; set; }

    /// <summary>Rule classification: "Technical", "Administrative", or "Physical".</summary>
    public required string Category { get; set; }

    /// <summary>Impact level if failed: "Critical", "High", "Medium", "Low".</summary>
    public required string Severity { get; set; }

    /// <summary>
    /// Human-readable description of why the rule failed; <c>null</c> when <see cref="Passed"/> is <c>true</c>.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>HIPAA section this rule enforces (e.g., "§164.312(a)(2)(iii)").</summary>
    public required string HipaaReference { get; set; }

    /// <summary>Actionable remediation guidance when the rule fails.</summary>
    public string? RemediationGuidance { get; set; }

    /// <summary>UTC timestamp when this evaluation was performed.</summary>
    public DateTime EvaluatedAtUtc { get; set; }
}
