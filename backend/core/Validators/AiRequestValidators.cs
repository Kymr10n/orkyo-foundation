using Api.Models;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Shape guard for the workspace's AI key. The prefix and length checks are cheap and
/// catch the common mistake — a key pasted from the wrong provider — at the boundary,
/// before it is encrypted and stored. The service repeats the check because it is also
/// reachable from code paths that do not run validators.
/// </summary>
public class SaveAiCredentialRequestValidator : AbstractValidator<SaveAiCredentialRequest>
{
    /// <summary>Anthropic keys carry this prefix.</summary>
    private const string AnthropicKeyPrefix = "sk-ant-";

    public SaveAiCredentialRequestValidator()
    {
        RuleFor(x => x.ApiKey)
            .NotEmpty().WithMessage("An API key is required.")
            .MinimumLength(20).WithMessage("That key is too short to be an Anthropic API key.")
            .MaximumLength(512)
            .Must(key => key is not null && key.Trim().StartsWith(AnthropicKeyPrefix, StringComparison.Ordinal))
            .WithMessage($"An Anthropic API key starts with '{AnthropicKeyPrefix}'.");
    }
}

/// <summary>
/// Shape guard for a per-user token allowance. Null is meaningful — it means no limit —
/// so only the negative case is rejected. The upper bound is a sanity cap: a limit larger
/// than any plausible month's usage is far more likely to be a typo than an intention.
/// </summary>
public class SaveAiAllowanceRequestValidator : AbstractValidator<SaveAiAllowanceRequest>
{
    private const long MaxPlausibleMonthlyTokens = 1_000_000_000;

    public SaveAiAllowanceRequestValidator()
    {
        RuleFor(x => x.MonthlyTokenLimit)
            .GreaterThanOrEqualTo(0).WithMessage("A token limit cannot be negative.")
            .LessThanOrEqualTo(MaxPlausibleMonthlyTokens)
            .WithMessage("That token limit looks like a mistake. Leave it empty for no limit.")
            .When(x => x.MonthlyTokenLimit.HasValue);
    }
}

/// <summary>
/// Shape guard for a saved conversation. The two payloads are the client's own shape and
/// are not inspected here — only their type and the title are. Size is checked in
/// <c>AiConversationService</c>, where the transcript ceiling already lives, so the limit
/// is stated once.
/// </summary>
public class SaveAiConversationRequestValidator : AbstractValidator<SaveAiConversationRequest>
{
    public SaveAiConversationRequestValidator()
    {
        // Title length and emptiness are deliberately NOT checked here. The client derives
        // the title from the first message, so rejecting a long or blank one would fail a
        // save the person did nothing wrong to cause. The service normalises instead —
        // one rule, in one place.


        // An array is what the panel keeps and what a restore expects; anything else
        // would restore into a broken conversation rather than fail here.
        RuleFor(x => x.Entries)
            .Must(e => e.ValueKind == System.Text.Json.JsonValueKind.Array)
            .WithMessage("Entries must be a list.");

        RuleFor(x => x.Transcript)
            .Must(t => t.ValueKind == System.Text.Json.JsonValueKind.Array)
            .WithMessage("The transcript must be a list.");
    }
}
