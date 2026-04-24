using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Coding;

// ─────────────────────────────────────────────────────────────────────────────
// Service-layer result/request types (US_051, AC-1, AC-2, AC-3, AC-4)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Aggregated payer validation result for a patient's current code set.</summary>
public sealed record PayerValidationRunResult
{
    public string?                             PayerName          { get; init; }
    public bool                                IsCmsDefault       { get; init; }
    public IReadOnlyList<PayerViolationItem>    Violations         { get; init; } = [];
    public IReadOnlyList<DenialRiskItem>        DenialRisks        { get; init; } = [];
    public IReadOnlyList<BundlingViolationItem> BundlingViolations { get; init; } = [];
}

/// <summary>A single payer rule violation detected for a patient's code set.</summary>
public sealed record PayerViolationItem
{
    public Guid                          ViolationId       { get; init; }
    public Guid                          RuleId            { get; init; }
    public PayerRuleSeverity             Severity          { get; init; }
    public string                        Description       { get; init; } = string.Empty;
    public string                        DenialReason      { get; init; } = string.Empty;
    public IReadOnlyList<string>         AffectedCodes     { get; init; } = [];
    public IReadOnlyList<CorrectiveItem> CorrectiveActions { get; init; } = [];
    public bool                          IsCmsDefault      { get; init; }
}

/// <summary>A suggested corrective action for a violation.</summary>
public sealed record CorrectiveItem
{
    public string  ActionType    { get; init; } = string.Empty;
    public string  Description   { get; init; } = string.Empty;
    public string? SuggestedCode { get; init; }
}

/// <summary>A claim denial risk assessment for a code or code pair.</summary>
public sealed record DenialRiskItem
{
    public string                        RiskLevel            { get; init; } = string.Empty;
    public IReadOnlyList<string>         CodePair             { get; init; } = [];
    public string                        DenialReason         { get; init; } = string.Empty;
    public decimal?                      HistoricalDenialRate { get; init; }
    public IReadOnlyList<CorrectiveItem> CorrectiveActions    { get; init; } = [];
}

/// <summary>An NCCI bundling rule violation for a CPT code pair.</summary>
public sealed record BundlingViolationItem
{
    public string                Column1Code         { get; init; } = string.Empty;
    public string                Column2Code         { get; init; } = string.Empty;
    public BundlingEditType      EditType            { get; init; }
    public IReadOnlyList<string> ApplicableModifiers { get; init; } = [];
    public string                Description         { get; init; } = string.Empty;
}

/// <summary>Conflict resolution input — staff decision when payer rule conflicts with clinical docs.</summary>
public sealed record ConflictResolutionRecord
{
    public Guid    ViolationId    { get; init; }
    public Guid    PatientId      { get; init; }
    public string  ResolutionType { get; init; } = string.Empty;
    public string  Justification  { get; init; } = string.Empty;
    public string? SelectedCode   { get; init; }
}

/// <summary>Result returned after a conflict resolution is recorded.</summary>
public sealed record ConflictResolutionRunResult
{
    public Guid     ViolationId      { get; init; }
    public string   ResolutionStatus { get; init; } = string.Empty;
    public DateTime ResolvedAt       { get; init; }
}

/// <summary>A single code entry in a multi-code assignment request.</summary>
public sealed record CodeAssignmentItem
{
    public string   CodeValue     { get; init; } = string.Empty;
    public CodeType CodeType      { get; init; }
    public string   Description   { get; init; } = string.Empty;
    public string   Justification { get; init; } = string.Empty;
    public int      SequenceOrder { get; init; }
}

/// <summary>Result of a multi-code assignment run.</summary>
public sealed record MultiCodeAssignmentRunResult
{
    public Guid                            PatientId     { get; init; }
    public IReadOnlyList<AssignedCodeItem> AssignedCodes { get; init; } = [];
}

/// <summary>A single successfully assigned code.</summary>
public sealed record AssignedCodeItem
{
    public Guid                  CodeId                { get; init; }
    public string                CodeValue             { get; init; } = string.Empty;
    public CodeType              CodeType              { get; init; }
    public string                Description           { get; init; } = string.Empty;
    public int                   SequenceOrder         { get; init; }
    public PayerValidationStatus PayerValidationStatus { get; init; }
}
