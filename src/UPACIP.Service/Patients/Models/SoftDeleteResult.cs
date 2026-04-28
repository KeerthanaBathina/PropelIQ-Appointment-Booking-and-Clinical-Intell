namespace UPACIP.Service.Patients.Models;

/// <summary>
/// Result of a patient soft-delete operation (US_087 AC-1, edge case 1).
///
/// When <see cref="Success"/> is <c>false</c>, <see cref="BlockedReason"/> provides a
/// human-readable explanation and <see cref="ActiveDependencies"/> lists every category
/// of dependent data that must be resolved before the delete can proceed.
/// </summary>
public sealed class SoftDeleteResult
{
    /// <summary><c>true</c> when the soft-delete was applied; <c>false</c> when blocked.</summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Human-readable reason the operation was blocked.
    /// <c>null</c> when <see cref="Success"/> is <c>true</c>.
    /// </summary>
    public string? BlockedReason { get; init; }

    /// <summary>
    /// Per-category list of active dependencies that block the deletion.
    /// Empty when <see cref="Success"/> is <c>true</c>.
    /// </summary>
    public IReadOnlyList<ActiveDependency> ActiveDependencies { get; init; }
        = Array.Empty<ActiveDependency>();

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>Creates a successful result.</summary>
    public static SoftDeleteResult Succeeded() => new() { Success = true };

    /// <summary>Creates a blocked result with the given reason and dependency list.</summary>
    public static SoftDeleteResult Blocked(
        string                         reason,
        IReadOnlyList<ActiveDependency> dependencies)
        => new()
        {
            Success             = false,
            BlockedReason       = reason,
            ActiveDependencies  = dependencies,
        };

    /// <summary>Creates a blocked result with a single reason and no dependency list.</summary>
    public static SoftDeleteResult Blocked(string reason)
        => new() { Success = false, BlockedReason = reason };
}

/// <summary>
/// Describes a category of active dependent records that prevent a patient soft-delete
/// (US_087 edge case 1).
/// </summary>
public sealed class ActiveDependency
{
    /// <summary>Entity type name (e.g. "Appointment", "IntakeData", "ClinicalDocument").</summary>
    public required string EntityType { get; init; }

    /// <summary>Number of active records in this category.</summary>
    public required int Count { get; init; }

    /// <summary>
    /// Short description of the blocking dependency
    /// (e.g. "3 scheduled appointments", "1 document queued for AI processing").
    /// </summary>
    public required string Detail { get; init; }
}
