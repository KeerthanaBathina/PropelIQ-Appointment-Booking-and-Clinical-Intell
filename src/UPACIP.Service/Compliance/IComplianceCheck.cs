namespace UPACIP.Service.Compliance;

/// <summary>
/// Contract for a single HIPAA technical safeguard verification check (US_093, AC-1).
/// Each implementation verifies one technical control and returns a structured result
/// with pass/fail status and human-readable evidence.
/// </summary>
public interface IComplianceCheck
{
    /// <summary>Human-readable name of the control being verified.</summary>
    string ControlName { get; }

    /// <summary>Compliance category: "Technical", "Administrative", or "Physical".</summary>
    string ControlCategory { get; }

    /// <summary>HIPAA Security Rule section reference (e.g. "§164.312(a)(2)(iv)").</summary>
    string HipaaReference { get; }

    /// <summary>
    /// Executes the verification check and returns a structured result.
    /// Implementations must not throw — all failures should be captured in the result.
    /// </summary>
    Task<ComplianceCheckResult> ExecuteAsync(CancellationToken ct);
}
