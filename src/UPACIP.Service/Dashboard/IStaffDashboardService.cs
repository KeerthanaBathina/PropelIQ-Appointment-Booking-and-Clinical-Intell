namespace UPACIP.Service.Dashboard;

/// <summary>
/// Aggregates stats, today's schedule, and pending tasks for the Staff Dashboard (US_057).
/// </summary>
public interface IStaffDashboardService
{
    /// <summary>
    /// Returns the aggregated dashboard payload for the requesting staff member.
    /// Results are cached per user per day with a 5-second TTL (US_057 AC-4).
    /// </summary>
    Task<StaffDashboardResponse> GetDashboardAsync(string userId, CancellationToken ct = default);
}
