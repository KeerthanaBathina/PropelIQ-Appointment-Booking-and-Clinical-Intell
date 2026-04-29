using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.Compliance;

/// <summary>
/// CRUD operations for HIPAA administrative safeguard policy documents (US_093, AC-2).
/// Provides evidence of documented and accessible security policies, training requirements,
/// and incident response procedures during HIPAA audits.
/// </summary>
public interface ICompliancePolicyService
{
    /// <summary>
    /// Creates a new policy version, superseding any previously active version for the same
    /// (PolicyType + Title) combination. Assigns <c>Version = 1</c> for brand-new policies
    /// or <c>max+1</c> for revisions.
    /// </summary>
    Task<CompliancePolicy> CreatePolicyAsync(CompliancePolicy policy, CancellationToken ct = default);

    /// <summary>
    /// Approves a policy draft, transitioning it to <c>Status = "Active"</c>.
    /// Records approver identity, approval timestamp, and effective date.
    /// </summary>
    Task<CompliancePolicy> ApprovePolicyAsync(Guid policyId, string approvedBy, CancellationToken ct = default);

    /// <summary>
    /// Returns all currently active policies (Status = "Active"), optionally filtered by type.
    /// Used during HIPAA audits to demonstrate documented and accessible safeguards (AC-2).
    /// </summary>
    Task<List<CompliancePolicy>> GetActivePoliciesAsync(string? policyType, CancellationToken ct = default);

    /// <summary>
    /// Returns all versions of a policy (by exact title), ordered newest-first.
    /// Provides the complete audit trail of policy revision history.
    /// </summary>
    Task<List<CompliancePolicy>> GetPolicyHistoryAsync(string title, CancellationToken ct = default);
}

/// <summary>
/// Scoped implementation of <see cref="ICompliancePolicyService"/>.
/// </summary>
public sealed class CompliancePolicyService : ICompliancePolicyService
{
    private readonly ApplicationDbContext           _db;
    private readonly ILogger<CompliancePolicyService> _logger;

    public CompliancePolicyService(
        ApplicationDbContext             db,
        ILogger<CompliancePolicyService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<CompliancePolicy> CreatePolicyAsync(
        CompliancePolicy policy,
        CancellationToken ct = default)
    {
        // Determine version number — 1 for new policies, max+1 for revisions.
        var existingMaxVersion = await _db.CompliancePolicies
            .Where(p => p.PolicyType == policy.PolicyType && p.Title == policy.Title)
            .MaxAsync(p => (int?)p.Version, ct) ?? 0;

        policy.Version      = existingMaxVersion + 1;
        policy.Status       = "Draft";
        policy.CreatedAtUtc = DateTime.UtcNow;

        // Supersede any currently active version of the same (type + title) combination.
        if (existingMaxVersion > 0)
        {
            var activeVersions = await _db.CompliancePolicies
                .Where(p => p.PolicyType == policy.PolicyType
                         && p.Title      == policy.Title
                         && p.Status     == "Active")
                .ToListAsync(ct);

            foreach (var prior in activeVersions)
                prior.Status = "Superseded";
        }

        _db.CompliancePolicies.Add(policy);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "COMPLIANCE_POLICY_CREATED: Type={Type}, Title={Title}, Version={Version}, CreatedBy={CreatedBy}",
            policy.PolicyType, policy.Title, policy.Version, policy.CreatedBy);

        return policy;
    }

    /// <inheritdoc/>
    public async Task<CompliancePolicy> ApprovePolicyAsync(
        Guid policyId,
        string approvedBy,
        CancellationToken ct = default)
    {
        var policy = await _db.CompliancePolicies.FindAsync(new object[] { policyId }, ct)
                     ?? throw new InvalidOperationException($"Policy {policyId} not found.");

        if (policy.Status == "Active")
            throw new InvalidOperationException($"Policy {policyId} is already active.");

        // Supersede any currently active version of the same (type + title) combination.
        var activeVersions = await _db.CompliancePolicies
            .Where(p => p.PolicyType == policy.PolicyType
                     && p.Title      == policy.Title
                     && p.Status     == "Active"
                     && p.Id         != policyId)
            .ToListAsync(ct);

        foreach (var prior in activeVersions)
            prior.Status = "Superseded";

        policy.Status        = "Active";
        policy.ApprovedBy    = approvedBy;
        policy.ApprovedAtUtc = DateTime.UtcNow;
        policy.EffectiveDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "COMPLIANCE_POLICY_APPROVED: Type={Type}, Title={Title}, Version={Version}, ApprovedBy={ApprovedBy}",
            policy.PolicyType, policy.Title, policy.Version, approvedBy);

        return policy;
    }

    /// <inheritdoc/>
    public async Task<List<CompliancePolicy>> GetActivePoliciesAsync(
        string? policyType,
        CancellationToken ct = default)
    {
        var query = _db.CompliancePolicies
            .Where(p => p.Status == "Active")
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(policyType))
            query = query.Where(p => p.PolicyType == policyType);

        return await query
            .OrderBy(p => p.PolicyType)
            .ThenByDescending(p => p.Version)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<List<CompliancePolicy>> GetPolicyHistoryAsync(
        string title,
        CancellationToken ct = default)
    {
        return await _db.CompliancePolicies
            .Where(p => p.Title == title)
            .OrderByDescending(p => p.Version)
            .ToListAsync(ct);
    }
}
