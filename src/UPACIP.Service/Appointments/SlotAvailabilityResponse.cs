namespace UPACIP.Service.Appointments;

/// <summary>
/// Response envelope for <c>GET /api/appointments/slots</c>.
///
/// Contains:
///   - <see cref="Slots"/>: flat list of all slot items in the requested date range.
///   - <see cref="Providers"/>: distinct providers present in the result set
///     (used by the frontend ProviderFilter dropdown — UXR-101).
///   - <see cref="DateSummary"/>: per-date availability count for calendar dot indicators
///     (AC-2 — dates with ≥1 available slot show a dot on the calendar).
/// </summary>
public sealed record SlotAvailabilityResponse(
    IReadOnlyList<SlotItem> Slots,
    IReadOnlyList<ProviderSummary> Providers,
    IReadOnlyList<DateAvailabilitySummary> DateSummary);

/// <summary>
/// A single 30-minute time slot within the availability grid.
/// </summary>
public sealed record SlotItem(
    /// <summary>Stable identifier for this slot (date + time + providerId composite).</summary>
    string SlotId,
    /// <summary>ISO-8601 date string (YYYY-MM-DD).</summary>
    string Date,
    /// <summary>Slot start time as "HH:mm" (24-hour).</summary>
    string StartTime,
    /// <summary>Slot end time as "HH:mm" (24-hour, StartTime + 30 min).</summary>
    string EndTime,
    /// <summary>Provider full name for display.</summary>
    string ProviderName,
    /// <summary>Provider unique identifier for filtering.</summary>
    string ProviderId,
    /// <summary>Appointment type label (e.g. "General Checkup").</summary>
    string AppointmentType,
    /// <summary>True when the slot has no conflicting appointment and is bookable.</summary>
    bool Available);

/// <summary>
/// Distinct provider returned in the response for the filter dropdown.
/// </summary>
public sealed record ProviderSummary(
    string ProviderId,
    string ProviderName);

/// <summary>
/// Per-date availability summary consumed by the calendar to render availability dots (AC-2).
/// </summary>
public sealed record DateAvailabilitySummary(
    /// <summary>ISO-8601 date string (YYYY-MM-DD).</summary>
    string Date,
    /// <summary>Number of available (bookable) slots on this date.</summary>
    int AvailableCount);
