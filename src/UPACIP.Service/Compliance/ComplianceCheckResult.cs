namespace UPACIP.Service.Compliance;

/// <summary>
/// Result of a single HIPAA technical safeguard verification check (US_093, AC-1).
/// </summary>
public sealed class ComplianceCheckResult
{
    /// <summary>Whether the control passed verification.</summary>
    public bool Passed { get; set; }

    /// <summary>Name of the control that was verified.</summary>
    public required string ControlName { get; set; }

    /// <summary>HIPAA Security Rule section reference.</summary>
    public required string HipaaReference { get; set; }

    /// <summary>Human-readable evidence of what was verified (populated on pass and fail).</summary>
    public required string Evidence { get; set; }

    /// <summary>Specific reason for failure. Null when the check passes.</summary>
    public string? FailureReason { get; set; }

    /// <summary>UTC timestamp when the check was executed.</summary>
    public DateTime VerifiedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Key-value pairs of detailed verification data
    /// (e.g. TLS version, cipher suite, encryption algorithm).
    /// </summary>
    public Dictionary<string, string> Details { get; set; } = new();
}
