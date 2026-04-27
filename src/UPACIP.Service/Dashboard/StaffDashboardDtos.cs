namespace UPACIP.Service.Dashboard;

/// <summary>
/// Top-level response envelope for GET /api/staff/dashboard (US_057, AC-1, AC-2, AC-3).
/// </summary>
public sealed record StaffDashboardResponse(
    DashboardStatsDto                      Stats,
    IReadOnlyList<DashboardScheduleItemDto> Schedule,
    IReadOnlyList<DashboardPendingTaskDto>  PendingTasks
);

/// <summary>
/// Aggregate counts shown in the stat cards on the Staff Dashboard (SCR-010, US_057 AC-1).
/// </summary>
public sealed record DashboardStatsDto(
    int TodayAppointments,
    int InQueue,
    int PendingReviews,
    int CompletedToday
);

/// <summary>
/// A single row in today's schedule table (US_057 AC-2).
/// </summary>
public sealed record DashboardScheduleItemDto(
    Guid      Id,
    Guid      PatientId,
    string    PatientName,
    DateTime  AppointmentTime,
    string?   AppointmentType,
    string    Status,
    int?      NoShowRiskScore,
    bool?     IsRiskEstimated
);

/// <summary>
/// A single pending work item in the task panel (US_057 AC-3).
/// Category values: "CodeApproval" | "ConflictResolution" | "DocumentReview".
/// TargetScreen values: "SCR-014" | "SCR-013" | "SCR-012".
/// </summary>
public sealed record DashboardPendingTaskDto(
    Guid   TaskId,
    string Category,
    Guid   PatientId,
    string PatientName,
    string Description,
    string TargetScreen
);
