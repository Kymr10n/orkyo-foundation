using System.Text.Json;
using Api.Models.Admin;

namespace Api.Models.Account;

/// <summary>
/// Everything Orkyo stores about one person, as the person themselves may download it
/// (GDPR Art. 15 access and Art. 20 portability). Secrets and tokens are never part of it:
/// a calendar feed is listed by label and date, not by its token.
/// </summary>
public sealed record PersonalDataExport
{
    public required string SchemaVersion { get; init; }
    public required DateTime ExportedAt { get; init; }
    public required PersonalProfile Profile { get; init; }
    public required IReadOnlyList<AdminUserIdentity> Identities { get; init; }
    public required IReadOnlyList<PersonalTermsAcceptance> TermsAcceptances { get; init; }
    public required IReadOnlyList<PersonalSession> Sessions { get; init; }
    public required IReadOnlyList<AdminUserMembership> Memberships { get; init; }
    public required IReadOnlyList<PersonalFeedback> Feedback { get; init; }
    /// <summary>The person's rows in each organization they belong to, one entry per organization.</summary>
    public required IReadOnlyList<PersonalOrganizationData> Organizations { get; init; }
}

public sealed record PersonalProfile
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Status { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public string? PendingEmail { get; init; }
    public string? LifecycleStatus { get; init; }
}

public sealed record PersonalTermsAcceptance
{
    public required string TosVersion { get; init; }
    public required DateTime AcceptedAt { get; init; }
    public string? AcceptedIp { get; init; }
    public string? AcceptedUserAgent { get; init; }
}

public sealed record PersonalSession
{
    public string? IpAddress { get; init; }
    public string? Browser { get; init; }
    public string? OperatingSystem { get; init; }
    public string? DeviceType { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime LastSeenAt { get; init; }
}

public sealed record PersonalFeedback
{
    public required string FeedbackType { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? PageUrl { get; init; }
    public required string Status { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed record PersonalOrganizationData
{
    public required string TenantSlug { get; init; }
    public required string TenantName { get; init; }
    public JsonElement? Preferences { get; init; }
    public required IReadOnlyList<PersonalSiteMembership> SiteMemberships { get; init; }
    public required IReadOnlyList<PersonalCalendarFeed> CalendarFeeds { get; init; }
    public required IReadOnlyList<PersonalAssistantConversation> AssistantConversations { get; init; }
    public required IReadOnlyList<PersonalAuditAction> AuditActions { get; init; }
}

public sealed record PersonalSiteMembership
{
    public required string SiteName { get; init; }
    public required string Role { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed record PersonalCalendarFeed
{
    public string? Label { get; init; }
    public string? SiteName { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public DateTime? RevokedAt { get; init; }
}

public sealed record PersonalAssistantConversation
{
    public required string Title { get; init; }
    public required JsonElement Transcript { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

/// <summary>An action the person took, without the event's metadata, which can name other people.</summary>
public sealed record PersonalAuditAction
{
    public required string Action { get; init; }
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public required DateTime CreatedAt { get; init; }
}
