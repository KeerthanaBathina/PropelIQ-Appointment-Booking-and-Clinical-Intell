using FluentValidation;

namespace UPACIP.Service.Admin;

/// <summary>
/// FluentValidation rules for <see cref="UpdateRiskConfigRequest"/>
/// (US_060 AC-3, AC-4, edge case: invalid threshold or weight values).
///
/// Rules:
///   - HighRiskThreshold:         0–100 integer.
///   - MediumRiskThreshold:       0–100 integer, must be strictly less than HighRiskThreshold.
///   - MinAppointmentsForAiScore: integer ≥ 1.
///   - ScoringParameters:         each weight ≥ 0.
///   - ScoringParameters sum:     PriorNoShowsWeight + CancellationHistoryWeight +
///                                AppointmentLeadTimeWeight must equal 1.0 (± 0.01 tolerance).
///
/// Returns 422 Unprocessable Entity via FluentValidation ASP.NET Core integration.
/// </summary>
public sealed class UpdateRiskConfigRequestValidator
    : AbstractValidator<UpdateRiskConfigRequest>
{
    private const decimal WeightTolerance = 0.01m;

    public UpdateRiskConfigRequestValidator()
    {
        // ── Thresholds ─────────────────────────────────────────────────────────
        RuleFor(x => x.HighRiskThreshold)
            .InclusiveBetween(0, 100)
            .WithMessage("HighRiskThreshold must be between 0 and 100.");

        RuleFor(x => x.MediumRiskThreshold)
            .InclusiveBetween(0, 100)
            .WithMessage("MediumRiskThreshold must be between 0 and 100.");

        // Cross-field: medium must be strictly less than high.
        RuleFor(x => x.MediumRiskThreshold)
            .LessThan(x => x.HighRiskThreshold)
            .WithMessage("MediumRiskThreshold must be less than HighRiskThreshold.");

        // ── Min appointments ───────────────────────────────────────────────────
        RuleFor(x => x.MinAppointmentsForAiScore)
            .GreaterThanOrEqualTo(1)
            .WithMessage("MinAppointmentsForAiScore must be at least 1.");

        // ── Scoring parameter weights ──────────────────────────────────────────
        RuleFor(x => x.ScoringParameters)
            .NotNull()
            .WithMessage("ScoringParameters must be provided.");

        When(x => x.ScoringParameters is not null, () =>
        {
            RuleFor(x => x.ScoringParameters.PriorNoShowsWeight)
                .GreaterThanOrEqualTo(0)
                .WithMessage("PriorNoShowsWeight must be ≥ 0.");

            RuleFor(x => x.ScoringParameters.CancellationHistoryWeight)
                .GreaterThanOrEqualTo(0)
                .WithMessage("CancellationHistoryWeight must be ≥ 0.");

            RuleFor(x => x.ScoringParameters.AppointmentLeadTimeWeight)
                .GreaterThanOrEqualTo(0)
                .WithMessage("AppointmentLeadTimeWeight must be ≥ 0.");

            // Sum-to-1.0 check with floating-point tolerance.
            RuleFor(x => x.ScoringParameters)
                .Must(WeightsSumToOne)
                .WithMessage(x =>
                {
                    var sum = x.ScoringParameters.PriorNoShowsWeight
                            + x.ScoringParameters.CancellationHistoryWeight
                            + x.ScoringParameters.AppointmentLeadTimeWeight;
                    return $"ScoringParameters weights must sum to 1.0 (current sum: {sum:F2}). " +
                           "Adjust PriorNoShowsWeight, CancellationHistoryWeight, or AppointmentLeadTimeWeight.";
                });
        });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool WeightsSumToOne(ScoringParametersDto p)
    {
        var sum = p.PriorNoShowsWeight
                + p.CancellationHistoryWeight
                + p.AppointmentLeadTimeWeight;
        return Math.Abs(sum - 1.0m) <= WeightTolerance;
    }
}
