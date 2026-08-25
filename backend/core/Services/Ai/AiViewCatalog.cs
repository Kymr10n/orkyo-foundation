namespace Api.Services.Ai;

/// <summary>
/// One place the assistant may take the person. Opening a view changes nothing in the
/// workspace — only what is on screen — so unlike a proposal it needs no confirmation.
/// </summary>
/// <param name="Id">What the model names. The client maps this to a route; no path ever
/// crosses the wire, so the model cannot navigate anywhere this catalog does not list.</param>
/// <param name="Description">Shown to the model, so it can pick without guessing.</param>
/// <param name="NeedsEntityId">True for the entity views, which open one record's dialog.</param>
public sealed record AiView(
    string Id,
    string Description,
    bool NeedsEntityId = false,
    bool RequiresEditor = false,
    bool RequiresAdmin = false);

/// <summary>
/// The closed set of views <c>open_view</c> accepts.
///
/// Mirrored by <c>frontend/src/components/assistant/view-catalog.ts</c>, which owns the
/// routes. The two lists are kept in step by a parity test rather than shared code: they are
/// different languages, and the ids are the contract between them.
/// </summary>
public static class AiViewCatalog
{
    /// <summary>Pages. Ids read as destinations, not routes, so the model is not tempted to invent paths.</summary>
    public static IReadOnlyList<AiView> Views { get; } =
    [
        new("scheduling", "The schedule itself — the utilization grid and calendar, where work is placed on resources."),
        new("requests", "The list of requests: the work waiting to be scheduled."),
        new("insights_overview", "Insights dashboard: headline numbers for the workspace."),
        new("insights_utilization", "Insights: how heavily resources are used over time."),
        new("insights_conflicts", "Insights: the conflicts list, grouped by kind."),
        new("organization", "People, departments and job titles."),
        new("stations", "Stations: the placeable resources, such as machines and workstations."),
        new("assets", "Assets: resources that are not placed on a floorplan, such as tools."),
        new("floorplan", "The floorplan, where stations are positioned."),

        new("settings_criteria", "Settings: the criteria resources can satisfy.", RequiresEditor: true),
        new("settings_templates", "Settings: request templates.", RequiresEditor: true),
        new("settings_scheduling", "Settings: scheduling rules and working hours.", RequiresEditor: true),

        new("admin_sites", "Administration: the workspace's sites.", RequiresAdmin: true),
        new("admin_users", "Administration: members and their roles.", RequiresAdmin: true),
        new("admin_ai_assistant", "Administration: the assistant's API key and per-person budgets.", RequiresAdmin: true),
        new("configuration_resource_types", "Configuration: the resource types this workspace defines.", RequiresAdmin: true),

        // Entity views open one record's edit dialog. They ride the app's existing
        // ?edit=<id> convention, so they need no dialog plumbing of their own.
        // Only records whose route is fixed. A resource's route depends on its type, which
        // would need the client's resource-type list to resolve — worth adding, not worth
        // holding up the rest.
        new("request", "One request, opened for editing. Needs entityId.", NeedsEntityId: true),
        new("site", "One site, opened for editing. Needs entityId.", NeedsEntityId: true, RequiresAdmin: true),
        new("template", "One request template, opened for editing. Needs entityId.", NeedsEntityId: true, RequiresEditor: true),
        new("criterion", "One criterion, opened for editing. Needs entityId.", NeedsEntityId: true, RequiresEditor: true),
    ];

    private static readonly Dictionary<string, AiView> ById =
        Views.ToDictionary(v => v.Id, StringComparer.Ordinal);

    public static AiView? Find(string? id) =>
        id is not null && ById.TryGetValue(id, out var view) ? view : null;

    /// <summary>What this person is allowed to be taken to — the same gate the pages enforce.</summary>
    public static IReadOnlyList<AiView> For(bool canEdit, bool isAdmin) =>
        Views.Where(v => (!v.RequiresEditor || canEdit) && (!v.RequiresAdmin || isAdmin)).ToList();

    public static bool IsAllowed(AiView view, bool canEdit, bool isAdmin) =>
        (!view.RequiresEditor || canEdit) && (!view.RequiresAdmin || isAdmin);
}
