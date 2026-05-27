using Api.Security;

namespace Api.Reporting;

public sealed record ReportDefinition(
    string Key,
    string Title,
    string Description,
    string Category,
    TenantRole MinimumRole);

public sealed record ReportEmbedTokenResult(
    string ReportKey,
    string EmbedUrl,
    string Token,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Static catalogue of MVP reports. Stored in code, not a database table —
/// the catalogue is a product contract and changes are versioned with the app.
/// </summary>
public static class ReportCatalogue
{
    public static readonly IReadOnlyList<ReportDefinition> All = new[]
    {
        new ReportDefinition(
            Key: "space_utilization",
            Title: "Space Utilization",
            Description: "Track how spaces are used over time — duration, frequency, and capacity coverage.",
            Category: "Spaces",
            MinimumRole: TenantRole.Viewer),

        new ReportDefinition(
            Key: "request_pipeline",
            Title: "Request Pipeline",
            Description: "Monitor request status distribution and scheduling throughput over time.",
            Category: "Requests",
            MinimumRole: TenantRole.Viewer),

        new ReportDefinition(
            Key: "allocation_conflicts",
            Title: "Allocation Conflicts",
            Description: "Identify time-overlapping assignments for exclusively-booked resources.",
            Category: "Resources",
            MinimumRole: TenantRole.Viewer),
    };

    public static ReportDefinition? Find(string key) =>
        All.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
}
