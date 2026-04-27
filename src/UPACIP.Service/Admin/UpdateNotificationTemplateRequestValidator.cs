using FluentValidation;
using System.Text.RegularExpressions;

namespace UPACIP.Service.Admin;

/// <summary>
/// FluentValidation rules for <see cref="UpdateNotificationTemplateByIdRequest"/>
/// (US_060 AC-1, AC-2, edge case: invalid variable placeholders).
///
/// Rules:
///   - Channel: required, must be one of Email | SMS | Email + SMS | In-App.
///   - Subject: required and max 200 chars when Channel is Email or "Email + SMS".
///   - BodyTemplate: required, max 5 000 chars, all {{...}} tokens must be in
///     the allowed set {patient_name, date, time, provider}.
///   - Status: required, must be one of Active | Draft | Disabled.
///
/// Returns 422 Unprocessable Entity (via FluentValidation ASP.NET Core integration)
/// with per-field errors listing any unrecognised variable names.
/// </summary>
public sealed class UpdateNotificationTemplateRequestValidator
    : AbstractValidator<UpdateNotificationTemplateByIdRequest>
{
    private static readonly HashSet<string> AllowedChannels =
        ["Email", "SMS", "Email + SMS", "In-App"];

    private static readonly HashSet<string> AllowedStatuses =
        ["Active", "Draft", "Disabled"];

    /// <summary>
    /// Variable placeholder names that are recognised by the notification pipeline.
    /// Any <c>{{token}}</c> in the body that does NOT appear in this set is invalid.
    /// </summary>
    private static readonly HashSet<string> AllowedTokens =
        ["patient_name", "date", "time", "provider"];

    private static readonly Regex PlaceholderRegex =
        new(@"\{\{(\w+)\}\}", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public UpdateNotificationTemplateRequestValidator()
    {
        // ── Channel ────────────────────────────────────────────────────────────
        RuleFor(x => x.Channel)
            .NotEmpty()
            .WithMessage("Channel is required.")
            .Must(c => AllowedChannels.Contains(c))
            .WithMessage($"Channel must be one of: {string.Join(", ", AllowedChannels)}.");

        // ── Subject (required for email channels) ─────────────────────────────
        RuleFor(x => x.Subject)
            .NotEmpty()
            .WithMessage("Subject is required for Email templates.")
            .MaximumLength(200)
            .WithMessage("Subject must not exceed 200 characters.")
            .When(x => x.Channel == "Email" || x.Channel == "Email + SMS");

        // ── BodyTemplate ───────────────────────────────────────────────────────
        RuleFor(x => x.BodyTemplate)
            .NotEmpty()
            .WithMessage("BodyTemplate is required.")
            .MaximumLength(5_000)
            .WithMessage("BodyTemplate must not exceed 5 000 characters.");

        RuleFor(x => x.BodyTemplate)
            .Must(HaveOnlyKnownTokens)
            .WithMessage(x =>
            {
                var unknown = ExtractUnknownTokens(x.BodyTemplate);
                return $"BodyTemplate contains unrecognised variable(s): {string.Join(", ", unknown.Select(t => $"{{{{{t}}}}}"))}. " +
                       $"Allowed: {string.Join(", ", AllowedTokens.Select(t => $"{{{{{t}}}}}"))}";
            })
            .When(x => !string.IsNullOrEmpty(x.BodyTemplate));

        // ── Status ─────────────────────────────────────────────────────────────
        RuleFor(x => x.Status)
            .NotEmpty()
            .WithMessage("Status is required.")
            .Must(s => AllowedStatuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", AllowedStatuses)}.");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool HaveOnlyKnownTokens(string body)
        => ExtractUnknownTokens(body).Count == 0;

    private static IReadOnlyList<string> ExtractUnknownTokens(string body)
    {
        var matches = PlaceholderRegex.Matches(body ?? string.Empty);
        return matches
            .Select(m => m.Groups[1].Value)
            .Where(token => !AllowedTokens.Contains(token))
            .Distinct()
            .ToList();
    }
}
