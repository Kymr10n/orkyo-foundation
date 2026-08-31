namespace Api.Security;

/// <summary>
/// Scopes an API access token can carry. Unlike the reporting token's single hardcoded value,
/// these are chosen at creation and validated against this list — an unknown scope is rejected
/// rather than stored, so a typo cannot silently produce a token that grants nothing (or, worse,
/// a scope string a future check reads loosely).
///
/// A scope maps to a <see cref="TenantRole"/> in <see cref="ScopeToRole"/>, which is what lets an
/// automated caller reuse the tenant role checks a human goes through instead of a parallel
/// permission system.
/// </summary>
public static class PlatformApiScopes
{
    /// <summary>Read the schedule: requests, resources, conflicts.</summary>
    public const string ScheduleRead = "schedule:read";

    /// <summary>Change the schedule: reschedule requests, assign resources.</summary>
    public const string ScheduleWrite = "schedule:write";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { ScheduleRead, ScheduleWrite };

    /// <summary>Scopes are stored and transmitted space-delimited, as OAuth does.</summary>
    public static string Join(IEnumerable<string> scopes) => string.Join(' ', scopes);

    public static string[] Split(string scopes) =>
        scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool AreAllKnown(IEnumerable<string> scopes) => scopes.All(All.Contains);

    /// <summary>
    /// The effective tenant role a token acts with. Write implies read, so a write-scoped token is
    /// an Editor; a read-only token is a Viewer. Nothing maps to Admin: administration is a human
    /// surface, and no v1 scope grants it.
    /// </summary>
    public static TenantRole ScopeToRole(string scopes)
    {
        var granted = Split(scopes);
        if (granted.Contains(ScheduleWrite, StringComparer.Ordinal)) return TenantRole.Editor;
        if (granted.Contains(ScheduleRead, StringComparer.Ordinal)) return TenantRole.Viewer;
        return TenantRole.None;
    }
}
