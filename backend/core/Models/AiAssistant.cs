namespace Api.Models;

/// <summary>
/// The workspace's stored AI credential, as the API exposes it. The key itself is
/// deliberately absent: it is written once and only ever read server-side by the chat
/// proxy. <see cref="KeyHint"/> is the non-secret tail of the key so an admin can tell
/// which key is configured without the API ever handing it back.
/// </summary>
public sealed record AiCredentialStatus
{
    public bool Configured { get; init; }
    public string Provider { get; init; } = AiProviders.Anthropic;
    public string? KeyHint { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? LastVerifiedAt { get; init; }
}

/// <summary>Providers this application can talk to. One, deliberately — see the plan's KISS stance.</summary>
public static class AiProviders
{
    public const string Anthropic = "anthropic";
}

/// <summary>Request body for storing or replacing the workspace's AI key.</summary>
public sealed record SaveAiCredentialRequest
{
    public string ApiKey { get; init; } = "";
}

/// <summary>Result of a credential probe against the provider. Never carries the key.</summary>
public sealed record AiCredentialTestResult
{
    public bool Ok { get; init; }
    /// <summary>One of: <c>invalid_key</c>, <c>network</c>, <c>model_unavailable</c>, <c>not_configured</c>. Null on success.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// One user's permission to use the assistant, plus what they have spent this month.
/// A user with no allowance row cannot use the assistant at all — see
/// <see cref="AiAccessDecision"/>.
/// </summary>
public sealed record AiUserAllowance
{
    public Guid UserId { get; init; }
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
    /// <summary>Null means unlimited. Zero means explicitly blocked.</summary>
    public long? MonthlyTokenLimit { get; init; }
    public long UsedInputTokens { get; init; }
    public long UsedOutputTokens { get; init; }
    public int UsedTurns { get; init; }
    /// <summary>True when the row exists — i.e. the admin granted this user access.</summary>
    public bool Granted { get; init; }

    public long UsedTotalTokens => UsedInputTokens + UsedOutputTokens;
}

/// <summary>Request body for granting or changing one user's allowance.</summary>
public sealed record SaveAiAllowanceRequest
{
    /// <summary>Null means unlimited. Zero blocks the user while keeping the grant visible.</summary>
    public long? MonthlyTokenLimit { get; init; }
}

/// <summary>
/// Why a user may or may not run an assistant turn right now. Computed per request from
/// the entitlement, the stored credential, the caller's role, and this month's usage.
/// </summary>
public sealed record AiAccessDecision
{
    public bool Allowed { get; init; }
    /// <summary>One of: <c>not_entitled</c>, <c>not_configured</c>, <c>not_allowed</c>, <c>allowance_exhausted</c>. Null when allowed.</summary>
    public string? Reason { get; init; }
    /// <summary>Null means unlimited (admins, or an explicit unlimited grant).</summary>
    public long? MonthlyTokenLimit { get; init; }
    public long UsedTotalTokens { get; init; }

    public static AiAccessDecision Deny(string reason) => new() { Allowed = false, Reason = reason };
}

/// <summary>What the assistant surface reports to a member so the UI knows whether to offer it.</summary>
public sealed record AiStatus
{
    public bool Available { get; init; }
    public string? Reason { get; init; }
    public long? MonthlyTokenLimit { get; init; }
    public long UsedTotalTokens { get; init; }
}
