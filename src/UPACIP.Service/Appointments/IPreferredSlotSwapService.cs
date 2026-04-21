namespace UPACIP.Service.Appointments;

/// <summary>
/// Evaluates newly available slots against appointments that have registered preferred-slot
/// criteria, and either automatically swaps eligible appointments or generates manual-confirmation
/// offers for slots opening within 24 hours (US_021).
///
/// Contracts:
///   - AC-1 / AC-2 : Automatic swap moves appointment, releases original slot ≤1 min, notifies.
///   - AC-3         : Staff-disabled accounts are skipped; reason is logged and audited.
///   - AC-4         : Multiple candidates ordered by wait time (longest first) then no-show risk (lowest first).
///   - AC-5         : Slots opening &lt;24 hours ahead skip auto-swap; a manual-confirmation notification is sent.
///   - EC-1         : Optimistic-concurrency conflicts retry the next eligible candidate — never left unresolved.
///   - EC-2         : Arrived / in-visit appointments are excluded from swap consideration.
/// </summary>
public interface IPreferredSlotSwapService
{
    /// <summary>
    /// Evaluates all eligible appointment candidates for the newly opened <paramref name="openedSlot"/>
    /// and attempts to swap the highest-priority candidate, retrying on conflict.
    ///
    /// Idempotent — already-swapped or non-matching entries are skipped without side-effects.
    /// </summary>
    /// <param name="openedSlot">Slot that just became available (from cancellation or new availability).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A list of results, one per evaluated candidate (including skipped and conflicted outcomes).
    /// </returns>
    Task<IReadOnlyList<PreferredSlotSwapResult>> EvaluateAndSwapAsync(
        SlotItem          openedSlot,
        CancellationToken cancellationToken = default);
}
