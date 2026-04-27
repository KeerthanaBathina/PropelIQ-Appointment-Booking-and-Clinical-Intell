namespace UPACIP.Service.Queue;

/// <summary>
/// Query parameters for the paginated GET /api/queue/today endpoint (US_053, EC-1).
///
/// All filters are optional — omitting a filter returns all entries for that dimension.
/// Page and PageSize are validated by FluentValidation via <see cref="QueueFilterParamsValidator"/>.
/// </summary>
public sealed record QueueFilterParams
{
    /// <summary>
    /// Optional provider name filter (exact case-insensitive match against
    /// <c>Appointment.ProviderName</c>). Pass <c>"all"</c> or <c>null</c> to skip.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Optional appointment type filter (case-insensitive match against
    /// <c>Appointment.AppointmentType</c>, e.g. "Checkup", "Follow-up").
    /// Pass <c>"all"</c> or <c>null</c> to skip (US_056 AC-2).
    /// </summary>
    public string? AppointmentType { get; init; }

    /// <summary>
    /// Optional queue status filter (e.g. "waiting", "in_visit", "no_show").
    /// Pass <c>"all"</c> or <c>null</c> to skip. Values must match <see cref="QueueStatusStrings"/>.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>1-based page number. Defaults to 1 (EC-1).</summary>
    public int Page { get; init; } = 1;

    /// <summary>Maximum entries per page. Defaults to 25 (EC-1).</summary>
    public int PageSize { get; init; } = 25;

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>True when the provider filter should be applied.</summary>
    public bool HasProviderFilter
        => !string.IsNullOrWhiteSpace(Provider) &&
           !Provider.Equals("all", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the appointment type filter should be applied.</summary>
    public bool HasAppointmentTypeFilter
        => !string.IsNullOrWhiteSpace(AppointmentType) &&
           !AppointmentType.Equals("all", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the status filter should be applied.</summary>
    public bool HasStatusFilter
        => !string.IsNullOrWhiteSpace(Status) &&
           !Status.Equals("all", StringComparison.OrdinalIgnoreCase);
}
