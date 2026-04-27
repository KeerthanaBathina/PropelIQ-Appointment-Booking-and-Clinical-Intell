namespace UPACIP.Service.Admin;

/// <summary>
/// Risk configuration operations with scoring parameter management (US_060 AC-3, AC-4).
///
/// Separate from <see cref="IConfigurationService.GetRiskThresholdsAsync"/> /
/// <see cref="IConfigurationService.UpdateRiskThresholdsAsync"/> to encapsulate the
/// extended domain logic: scoring parameter weights, sum-to-1.0 enforcement, and the
/// deferred-recalculation flag that signals the background batch job.
/// </summary>
public interface IRiskConfigService
{
    /// <summary>
    /// Returns the current risk configuration including scoring parameter weights
    /// and the deferred recalculation flag.
    /// </summary>
    Task<RiskConfigDto> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists updated risk thresholds and scoring parameters.
    ///
    /// <para>
    /// Sets <see cref="RiskConfigDto.RecalculationPending"/> to <c>true</c> so the
    /// background job recalculates risk scores for all active appointments in the
    /// next scheduled run (edge case: risk threshold changes on active appointments).
    /// </para>
    /// <para>
    /// Appends an <c>AuditLog</c> entry with previous and new values for admin
    /// attribution (AC-4).
    /// </para>
    /// </summary>
    Task<RiskConfigDto> UpdateAsync(
        UpdateRiskConfigRequest request,
        Guid                    adminUserId,
        CancellationToken       ct = default);
}
