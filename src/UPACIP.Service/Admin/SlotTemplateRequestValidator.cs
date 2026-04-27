using FluentValidation;

namespace UPACIP.Service.Admin;

/// <summary>
/// Validates individual <see cref="SlotTemplateBlockDto"/> records.
/// Rules:
///   - AppointmentType: required, max 50 characters.
///   - EndTime must be strictly after StartTime.
/// </summary>
public sealed class SlotTemplateBlockDtoValidator : AbstractValidator<SlotTemplateBlockDto>
{
    public SlotTemplateBlockDtoValidator()
    {
        RuleFor(x => x.AppointmentType)
            .NotEmpty()
            .WithMessage("AppointmentType is required.")
            .MaximumLength(50)
            .WithMessage("AppointmentType must not exceed 50 characters.");

        RuleFor(x => x.EndTime)
            .GreaterThan(x => x.StartTime)
            .WithMessage("EndTime must be after StartTime.");
    }
}

/// <summary>
/// Validates <see cref="UpsertSlotTemplateRequest"/> for
/// PUT /api/admin/config/slots/{providerId}/{dayOfWeek} (US_059 AC-1, AC-2).
///
/// Rules:
///   - Blocks: required, not empty, max 24 entries (one per hour).
///   - Each block individually valid (delegated to <see cref="SlotTemplateBlockDtoValidator"/>).
///   - Blocks must not overlap: no two blocks can share any minute of the day.
/// </summary>
public sealed class UpsertSlotTemplateRequestValidator : AbstractValidator<UpsertSlotTemplateRequest>
{
    private const int MaxBlocks = 24;

    public UpsertSlotTemplateRequestValidator()
    {
        RuleFor(x => x.Blocks)
            .NotNull()
            .WithMessage("Blocks must be provided.")
            .NotEmpty()
            .WithMessage("At least one time block is required.")
            .Must(b => b.Count <= MaxBlocks)
            .WithMessage($"A template may not exceed {MaxBlocks} blocks per day.");

        RuleForEach(x => x.Blocks)
            .SetValidator(new SlotTemplateBlockDtoValidator());

        // Non-overlapping check: for every pair of blocks, one must end before the other starts.
        RuleFor(x => x.Blocks)
            .Must(blocks =>
            {
                if (blocks is null || blocks.Count < 2) return true;
                var sorted = blocks.OrderBy(b => b.StartTime).ToList();
                for (var i = 0; i < sorted.Count - 1; i++)
                {
                    if (sorted[i].EndTime > sorted[i + 1].StartTime)
                        return false;
                }
                return true;
            })
            .WithMessage("Slot template blocks must not overlap in time.");
    }
}
