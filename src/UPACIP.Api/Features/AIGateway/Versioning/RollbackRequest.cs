using System.ComponentModel.DataAnnotations;

namespace UPACIP.Api.Features.AIGateway.Versioning;

/// <summary>
/// Request body for the <c>POST /api/admin/ai-gateway/versions/rollback</c> endpoint
/// (US_069 TASK_003, AIR-O05).
///
/// <para>Validated with <see cref="Required"/> annotations; the endpoint returns HTTP 400
/// for missing or empty fields before calling <see cref="IModelVersionRegistry.ExecuteRollback"/>.</para>
/// </summary>
/// <param name="Provider">
/// Provider to roll back: <c>"openai"</c> or <c>"anthropic"</c> (case-insensitive).
/// </param>
/// <param name="Reason">
/// Human-readable justification for the rollback required for the audit trail.
/// Must be non-empty.
/// </param>
public sealed record RollbackRequest(
    [Required] string Provider,
    [Required] [MinLength(1)] string Reason);
