namespace UPACIP.Service.Appointments;

/// <summary>
/// Response returned by <see cref="IAIIntakeSessionService.StartOrResumeSessionAsync"/> (AC-1, EC-2).
/// </summary>
public sealed record StartAIIntakeResponse
{
    public Guid SessionId { get; init; }
    public bool IsResumed { get; init; }
    public string GreetingMessage { get; init; } = string.Empty;
    public IReadOnlyList<AIIntakeTurnDto> History { get; init; } = [];
    public int CollectedCount { get; init; }
    public int TotalRequired { get; init; }
    public string? LastSavedAt { get; init; }
}

/// <summary>
/// Response returned by <see cref="IAIIntakeSessionService.SendMessageAsync"/> (AC-2, AC-5).
/// </summary>
public sealed record AIIntakeMessageResponse
{
    public string ReplyToPatient { get; init; } = string.Empty;
    public string FieldKey { get; init; } = string.Empty;
    public string? ExtractedValue { get; init; }
    public bool NeedsClarification { get; init; }
    public IReadOnlyList<string> ClarificationExamples { get; init; } = [];
    public int CollectedCount { get; init; }
    public int TotalRequired { get; init; }
    public bool SummaryReady { get; init; }
    public bool ShouldSwitchToManual { get; init; }
    public string LastSavedAt { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
}

/// <summary>
/// Response returned by <see cref="IAIIntakeSessionService.GetSummaryAsync"/> (AC-4).
/// </summary>
public sealed record AIIntakeSummaryResponse
{
    public string SummaryText { get; init; } = string.Empty;
    public IReadOnlyList<AIIntakeSummaryFieldDto> Fields { get; init; } = [];
    public int MandatoryCollectedCount { get; init; }
    public int MandatoryTotalCount { get; init; }
}

/// <summary>A single field entry in the summary table (AC-4 review).</summary>
public sealed record AIIntakeSummaryFieldDto
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public bool IsMandatory { get; init; }
    public bool IsEditable { get; init; }
}

/// <summary>
/// Response returned by <see cref="IAIIntakeSessionService.SwitchToManualAsync"/> (FL-004, US_028).
/// </summary>
public sealed record SwitchToManualResponse
{
    public IReadOnlyDictionary<string, string> PrefilledFields { get; init; }
        = new Dictionary<string, string>();

    public string Message { get; init; } = "Switching to manual form. Your progress has been saved.";
}

/// <summary>
/// Response returned by <see cref="IAIIntakeSessionService.CompleteSessionAsync"/> (AC-4, FR-029).
/// </summary>
public sealed record CompleteIntakeResponse
{
    public Guid IntakeDataId { get; init; }
    public string CompletedAt { get; init; } = string.Empty;
}

/// <summary>A single conversation turn for the history in <see cref="StartAIIntakeResponse"/>.</summary>
public sealed record AIIntakeTurnDto
{
    public string Id { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Timestamp { get; init; } = string.Empty;
    public IReadOnlyList<string>? ClarificationExamples { get; init; }
}
