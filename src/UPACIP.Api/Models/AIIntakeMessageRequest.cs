using System.ComponentModel.DataAnnotations;

namespace UPACIP.Api.Models;

/// <summary>
/// Request body for <c>POST /api/intake/sessions/{sessionId}/messages</c>.
/// Carries the patient's raw text reply for a single conversational turn (AC-2).
///
/// Payload size bound: maximum 1 000 characters — enforced by FluentValidation
/// and the AI layer sanitisation guardrail (AIR-S06, guardrails.json §MaxPatientInputLengthChars).
/// </summary>
public sealed record AIIntakeMessageRequest
{
    /// <summary>Patient's response text. Required. Maximum 1 000 characters.</summary>
    [Required]
    [MaxLength(1_000)]
    public string Content { get; init; } = string.Empty;
}

/// <summary>
/// Paginated request for manually switching from AI intake to the manual form.
/// Carries the session identifier so collected data can be pre-filled.
/// </summary>
public sealed record SwitchToManualRequest
{
    /// <summary>Active AI intake session to transfer data from. Required.</summary>
    [Required]
    public Guid SessionId { get; init; }
}
