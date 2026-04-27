using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Api.Features.AIGateway.Models;

namespace UPACIP.Api.Features.AIGateway.Services;

/// <summary>
/// Strict token-budget validation service for the AI Gateway (US_070 TASK_001, AIR-O01–AIR-O03).
///
/// <para>Unlike <see cref="ITokenBudgetEnforcementService"/> (which <em>truncates</em>
/// oversized prompts to fit within budget), this service performs a
/// <strong>reject-on-exceed</strong> check: if the prompt token count is above the
/// configured ceiling, <see cref="ValidateAsync"/> returns a <see cref="TokenBudgetResult"/>
/// with <c>IsWithinBudget = false</c> and the caller must surface a structured error to
/// the client <em>before</em> any provider call is made (AC-1).</para>
///
/// <para>Budget profiles (hard-coded presets, overridable via <c>appsettings.json</c>):</para>
/// <list type="table">
///   <listheader><term>Request type</term><description>Input / Output token limits</description></listheader>
///   <item><term>DocumentParsing</term><description>4 096 / 1 024 (AIR-O01)</description></item>
///   <item><term>ConversationalIntake</term><description>500 / 200 (AIR-O02)</description></item>
///   <item><term>MedicalCoding</term><description>2 048 / 500 (AIR-O03)</description></item>
/// </list>
/// </summary>
public interface ITokenBudgetValidator
{
    /// <summary>
    /// Counts the input tokens in <paramref name="request"/> and checks whether the count
    /// is at or below the configured budget ceiling for the request type.
    /// </summary>
    /// <param name="request">The AI request to validate. Only the <c>Prompt</c> and
    /// <c>RequestType</c> fields are inspected; no external calls are made.</param>
    /// <param name="cancellationToken">Cancellation token (forwarded to async sub-operations).</param>
    /// <returns>
    /// A <see cref="TokenBudgetResult"/> whose <c>IsWithinBudget</c> flag indicates whether
    /// the request may proceed. Never throws for budget violations — only throws on
    /// unrecoverable infrastructure failures.
    /// </returns>
    Task<TokenBudgetResult> ValidateAsync(
        AIRequest         request,
        CancellationToken cancellationToken = default);
}
