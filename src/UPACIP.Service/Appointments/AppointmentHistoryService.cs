using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Implements the patient appointment history retrieval (US_024, FR-024) with:
///
///   - Patient ownership enforcement via email claim lookup (OWASP A01 IDOR prevention).
///   - All statuses returned — Scheduled, Completed, Cancelled, NoShow — so no history
///     record is silently dropped (EC-2).
///   - Newest-first default ordering (AC-1) with optional ascending sort toggle (AC-2).
///   - Fixed page size of 10 with stable pagination metadata (AC-3).
///   - Lightweight EF Core projection: only columns needed by the SCR-007 table are fetched (NFR-004).
///   - 200 + empty payload for patients with no appointments; never throws for empty history (EC-1).
///   - Structured Serilog logging — email is not logged, only patient ID and result counts (NFR-017, NFR-035).
///
/// Implementation notes:
///   - <c>TotalCount</c> uses a separate <c>CountAsync</c> call on the filtered (pre-sort,
///     pre-page) query so the count is always consistent with the full history, not just the page.
///   - When the requested page exceeds <c>TotalPages</c>, an empty <c>Items</c> list is returned
///     with accurate <c>TotalCount</c> and <c>TotalPages</c> so the frontend can recalibrate.
///   - The sort direction string is normalised to lower-case before comparison to avoid
///     case-sensitivity bugs from query-string binding (AC-2).
/// </summary>
public sealed class AppointmentHistoryService : IAppointmentHistoryService
{
    private readonly ApplicationDbContext                  _db;
    private readonly ILogger<AppointmentHistoryService>   _logger;

    public AppointmentHistoryService(
        ApplicationDbContext                 db,
        ILogger<AppointmentHistoryService>   logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AppointmentHistoryResponse> GetHistoryAsync(
        string                  userEmail,
        AppointmentHistoryQuery query,
        CancellationToken       cancellationToken = default)
    {
        // ── 1. Resolve patient from JWT email (OWASP A01) ────────────────────
        // PatientId is NEVER accepted from query parameters or the request body.
        var patient = await _db.Patients
            .AsNoTracking()
            .Where(p => p.Email == userEmail && p.DeletedAt == null)
            .Select(p => new { p.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (patient is null)
        {
            _logger.LogWarning(
                "GetHistory: patient not found for the provided email claim.");
            return EmptyResponse(query.Page);
        }

        // ── 2. Base query: all statuses, patient-scoped (EC-2) ───────────────
        // No status filter — Cancelled and NoShow appointments must remain visible.
        var baseQuery = _db.Appointments
            .AsNoTracking()
            .Where(a => a.PatientId == patient.Id);

        // ── 3. Count total (pre-pagination, post-filter) ─────────────────────
        var totalCount = await baseQuery.CountAsync(cancellationToken);

        if (totalCount == 0)
        {
            _logger.LogDebug(
                "GetHistory: patient {PatientId} has no appointments.", patient.Id);
            return EmptyResponse(query.Page);
        }

        var totalPages = (int)Math.Ceiling(totalCount / (double)AppointmentHistoryQuery.PageSize);

        // ── 4. Apply sort (AC-1 default desc, AC-2 optional asc) ────────────
        // Direction comparison is case-insensitive (normalise to lower-case).
        var isAscending = string.Equals(
            query.SortDirection.Trim(),
            "asc",
            StringComparison.OrdinalIgnoreCase);

        var sorted = isAscending
            ? baseQuery.OrderBy(a => a.AppointmentTime)
            : baseQuery.OrderByDescending(a => a.AppointmentTime);

        // ── 5. Paginate ───────────────────────────────────────────────────────
        var skip = (query.Page - 1) * AppointmentHistoryQuery.PageSize;

        // ── 6. Project to DTO — no navigation properties loaded (NFR-004) ────
        var items = await sorted
            .Skip(skip)
            .Take(AppointmentHistoryQuery.PageSize)
            .Select(a => new AppointmentHistoryItemDto(
                a.Id,
                a.BookingReference ?? string.Empty,
                a.AppointmentTime,
                a.ProviderName    ?? "Unknown Provider",
                a.AppointmentType ?? "General",
                a.Status.ToString()))
            .ToListAsync(cancellationToken);

        _logger.LogDebug(
            "GetHistory: returned {Count}/{Total} appointments for patient {PatientId}, page={Page}, sort={Sort}.",
            items.Count, totalCount, patient.Id, query.Page, query.SortDirection);

        return new AppointmentHistoryResponse(
            Items:      items,
            TotalCount: totalCount,
            TotalPages: totalPages,
            Page:       query.Page,
            PageSize:   AppointmentHistoryQuery.PageSize);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an empty-state response with accurate metadata.
    /// Used when the patient has no appointments or does not exist (EC-1).
    /// </summary>
    private static AppointmentHistoryResponse EmptyResponse(int page)
        => new(
            Items:      [],
            TotalCount: 0,
            TotalPages: 0,
            Page:       page,
            PageSize:   AppointmentHistoryQuery.PageSize);
}
