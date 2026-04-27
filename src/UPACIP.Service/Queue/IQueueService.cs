namespace UPACIP.Service.Queue;

/// <summary>
/// Business-logic interface for arrival queue management (US_052, US_053).
///
/// All mutations return <see cref="QueueServiceResult{T}"/> so controllers can
/// map outcomes to HTTP status codes without catching exceptions.
/// </summary>
public interface IQueueService
{
    /// <summary>
    /// Returns today's queue entries sorted by priority (Urgent first) then appointment time.
    /// Cached in Redis with a 5-minute TTL (NFR-030, NFR-004). Cache-aside fallback to DB
    /// when Redis is unavailable (AC-4 cache bypass requirement).
    /// </summary>
    Task<QueueTodayResponse> GetTodayQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a filtered, sorted, paginated view of today's queue entries (US_053, EC-1).
    ///
    /// Sort order: urgent priority first, then ascending appointment time (AC-1).
    /// Cache strategy: per-filter granular Redis key via <see cref="UPACIP.Service.Caching.IQueueCacheService"/>,
    /// 5-minute TTL (NFR-004). Cache-aside fallback to DB on Redis miss/error.
    ///
    /// Response includes:
    ///   - Computed <c>WaitTimeMinutes</c> for each entry
    ///   - Average wait time across currently waiting patients (AC-4)
    ///   - Count of patients waiting over the configured threshold (default 30 min)
    ///   - UTC <c>LastUpdated</c> timestamp for the "Last updated" UI badge (EC-2)
    /// </summary>
    Task<QueuePagedResponseDto> GetTodayQueuePagedAsync(
        QueueFilterParams     filters,
        CancellationToken     cancellationToken = default);

    /// <summary>
    /// Marks a patient as arrived. Creates a new <c>QueueEntry</c> with
    /// <c>ArrivalTimestamp = DateTime.UtcNow</c> and <c>Status = Waiting</c> (AC-1).
    ///
    /// Returns <see cref="QueueOperationResult.Conflict"/> when an active entry already
    /// exists for the appointment (duplicate arrival prevention, 409 Conflict).
    /// Returns <see cref="QueueOperationResult.NotFound"/> when the appointment does not exist.
    /// </summary>
    Task<QueueServiceResult<QueueEntryDto>> MarkArrivalAsync(
        Guid   appointmentId,
        Guid   staffUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the status of a queue entry (e.g. to <c>cancelled</c>).
    /// When status is <c>cancelled</c>, also calls <c>AppointmentService.ReleaseSlotAsync</c>
    /// to mark the appointment slot available for walk-ins (AC-3).
    /// </summary>
    Task<QueueServiceResult<QueueEntryDto>> UpdateStatusAsync(
        Guid   queueId,
        string newStatus,
        Guid   staffUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Overrides a no-show entry to arrived-late status (edge case).
    /// Validates that the current appointment status is <c>NoShow</c>; returns
    /// <see cref="QueueOperationResult.UnprocessableStatusTransition"/> otherwise.
    /// Writes an audit log entry including the override reason and staff identity (TR-028).
    /// </summary>
    Task<QueueServiceResult<QueueEntryDto>> OverrideNoShowAsync(
        Guid   queueId,
        string reason,
        Guid   staffUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by <see cref="NoShowDetectionService"/> to mark appointments that have
    /// exceeded the 15-minute no-show threshold (AC-2).
    /// </summary>
    /// <param name="isDelayedDetection">
    /// Pass <c>true</c> during startup outage-recovery scan to flag entries as
    /// delayed-detection (edge case — service restarted after threshold had already elapsed).
    /// </param>
    Task MarkNoShowsAsync(bool isDelayedDetection = false, CancellationToken cancellationToken = default);

    // ── US_055 — Wait Threshold Configuration ───────────────────────────────

    /// <summary>
    /// Returns the current configurable wait time alert threshold in minutes (US_055 AC-3).
    /// Reads from Redis cache (60s TTL); falls back to <see cref="QueueSettings.WaitTimeThresholdMinutes"/>
    /// when the cache key is absent (default 30 min).
    /// </summary>
    Task<int> GetWaitThresholdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new wait time alert threshold (admin-only mutation, US_055 AC-3).
    /// Writes the value to Redis (60s TTL), invalidates the queue cache so all active
    /// views pick up the new threshold on next poll, and appends an audit log entry
    /// with <see cref="UPACIP.DataAccess.Enums.AuditAction.WaitThresholdConfigChanged"/>.
    /// </summary>
    Task UpdateWaitThresholdAsync(
        int   thresholdMinutes,
        Guid  adminUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    // ── US_054 — Priority Queue Management ──────────────────────────────────

    /// <summary>
    /// Sets the priority of a queue entry to Urgent or Normal, then recalculates all
    /// <c>QueuePosition</c> values for today's active queue (US_054 AC-1, AC-4):
    ///   Tier 1 — Urgent entries sorted by <c>ArrivalTimestamp</c> ASC  → positions 1…n
    ///   Tier 2 — Normal entries sorted by <c>ArrivalTimestamp</c> ASC  → positions n+1…m
    ///
    /// Writes a <c>QueueAuditLog</c> entry with the old and new priority + positions (AC-3).
    /// Invalidates the Redis cache so subsequent GET /queue/today reflects the new order.
    ///
    /// Returns <see cref="QueueOperationResult.NotFound"/> when <paramref name="queueId"/> does not exist.
    /// Returns <see cref="QueueOperationResult.UnprocessableStatusTransition"/> when the supplied
    /// priority string is not "urgent" or "normal".
    /// </summary>
    Task<QueueServiceResult<QueueReorderResponse>> SetPriorityAsync(
        Guid   queueId,
        string priority,
        Guid   staffUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a queue entry to a specific 1-based position within today's active queue,
    /// shifting all other entries to maintain contiguous ordering (US_054 AC-2, AC-3).
    ///
    /// Optimistic concurrency: loads the entry's current <c>Version</c>, saves, and catches
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>. On conflict,
    /// returns <see cref="QueueOperationResult.Conflict"/> with the current queue state so the
    /// caller can return 409 with the refreshed list (concurrent edit protection).
    ///
    /// Writes a <c>QueueAuditLog</c> entry with staff attribution, original position, and
    /// new position (AC-3). Invalidates the Redis cache after any successful reorder.
    ///
    /// Returns <see cref="QueueOperationResult.NotFound"/> when <paramref name="queueId"/> does not exist.
    /// </summary>
    Task<QueueServiceResult<QueueReorderResponse>> ReorderQueueAsync(
        Guid   queueId,
        int    newPosition,
        Guid   staffUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    // ── US_056 — Queue History Analytics ────────────────────────────────────

    /// <summary>
    /// Returns per-day aggregated queue metrics for the requested date range (US_056 AC-3).
    /// Results are cached in Redis with a 5-minute TTL per date-range key.
    ///
    /// Validates: <paramref name="startDate"/> ≤ <paramref name="endDate"/>;
    /// range ≤ 365 days. Returns a <c>null</c> result for invalid inputs (caller maps to 400).
    ///
    /// Returns an empty <c>Metrics</c> list (not null, not 404) when no queue data falls
    /// in the requested range. The <c>AvailableFromDate</c> field on the response indicates
    /// the earliest date that has data.
    /// </summary>
    Task<QueueHistoryResponse?> GetQueueHistoryAsync(
        string startDate,
        string endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports queue history for the requested date range as a UTF-8 CSV byte array (US_056 AC-4).
    /// Columns: Date, TotalEntries, PatientThroughput, NoShowCount, AvgWaitTimeMinutes.
    ///
    /// Validates: <paramref name="startDate"/> ≤ <paramref name="endDate"/>;
    /// range ≤ 365 days. Returns <c>null</c> for invalid inputs (caller maps to 400).
    /// </summary>
    Task<byte[]?> ExportQueueHistoryAsCsvAsync(
        string startDate,
        string endDate,
        CancellationToken cancellationToken = default);
}
