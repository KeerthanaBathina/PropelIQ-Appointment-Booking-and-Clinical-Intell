using OpenAI.Chat;
using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Providers.OpenAI;

/// <summary>
/// Static mapper that converts the AI Gateway's unified <see cref="AIRequest"/>
/// to OpenAI .NET SDK <see cref="ChatMessage"/> and <see cref="ChatCompletionOptions"/>
/// for use with <see cref="ChatClient"/> (US_068 TASK_001, AIR-O01, AIR-O02, AIR-O03).
///
/// Static methods enable isolated unit testing without adapter instantiation.
/// </summary>
public static class OpenAIRequestMapper
{
    /// <summary>
    /// Maps <paramref name="request"/> to an ordered list of <see cref="ChatMessage"/> objects
    /// ready for submission to <c>ChatClient.CompleteChatAsync</c>.
    ///
    /// Produces:
    /// <list type="bullet">
    ///   <item>A <see cref="SystemChatMessage"/> when <see cref="AIRequest.SystemMessage"/> is non-empty.</item>
    ///   <item>A <see cref="UserChatMessage"/> from <see cref="AIRequest.Prompt"/>.</item>
    /// </list>
    /// </summary>
    public static IList<ChatMessage> MapToMessages(AIRequest request)
    {
        var messages = new List<ChatMessage>(capacity: 2);

        if (!string.IsNullOrWhiteSpace(request.SystemMessage))
            messages.Add(new SystemChatMessage(request.SystemMessage));

        messages.Add(new UserChatMessage(request.Prompt));

        return messages;
    }

    /// <summary>
    /// Maps token budget and sampling temperature from <paramref name="request"/> to
    /// <see cref="ChatCompletionOptions"/>.
    ///
    /// <paramref name="effectiveMaxOutputTokens"/> is the pre-computed, budget-clamped
    /// output ceiling resolved by the caller before mapping.
    /// </summary>
    public static ChatCompletionOptions MapToOptions(AIRequest request, int effectiveMaxOutputTokens) =>
        new()
        {
            MaxOutputTokenCount = effectiveMaxOutputTokens,
            Temperature         = request.Temperature,
        };
}
