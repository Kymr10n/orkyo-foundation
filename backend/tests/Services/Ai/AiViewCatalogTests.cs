using Api.Security;
using Api.Services.Ai;
using Xunit;

namespace Orkyo.Foundation.Tests.Services.Ai;

/// <summary>
/// The view catalog is a contract with the browser: the model names an id, the client owns
/// the route. These pin the parts that would fail quietly — an id the client cannot resolve
/// moves nobody, and a role gate that leaks would offer someone a door they are refused at.
/// </summary>
public class AiViewCatalogTests
{
    /// <summary>
    /// Mirrors frontend/src/components/assistant/view-catalog.ts. Kept by hand because the
    /// two sides are different languages; this fixture is what makes drift fail loudly
    /// instead of silently refusing to open a view in production.
    /// </summary>
    private static readonly string[] ClientKnownIds =
    [
        "scheduling", "requests",
        "insights_overview", "insights_utilization", "insights_conflicts",
        "organization", "stations", "assets", "floorplan",
        "settings_criteria", "settings_templates", "settings_scheduling",
        "admin_sites", "admin_users", "admin_ai_assistant", "configuration_resource_types",
        "request", "site", "template", "criterion",
    ];

    [Fact]
    public void EveryServerViewIsOneTheClientCanRoute()
    {
        var missing = AiViewCatalog.Views.Select(v => v.Id).Except(ClientKnownIds).ToList();

        Assert.True(missing.Count == 0,
            $"The client cannot route: {string.Join(", ", missing)}. Add them to view-catalog.ts.");
    }

    [Fact]
    public void EveryClientViewExistsOnTheServer()
    {
        // The other direction matters too: a client route with no server view is dead code
        // the model can never reach.
        var orphaned = ClientKnownIds.Except(AiViewCatalog.Views.Select(v => v.Id)).ToList();

        Assert.True(orphaned.Count == 0, $"No server view for: {string.Join(", ", orphaned)}");
    }

    [Fact]
    public void ViewIdsAreUnique()
    {
        var ids = AiViewCatalog.Views.Select(v => v.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void AViewerIsOfferedNoEditorOrAdminDoor()
    {
        var offered = AiViewCatalog.For(canEdit: false, isAdmin: false);

        Assert.DoesNotContain(offered, v => v.RequiresEditor || v.RequiresAdmin);
        // Still useful: the member pages are the bulk of the catalog.
        Assert.Contains(offered, v => v.Id == "insights_conflicts");
    }

    [Fact]
    public void AnEditorGetsSettingsButNotAdministration()
    {
        var offered = AiViewCatalog.For(canEdit: true, isAdmin: false).Select(v => v.Id).ToList();

        Assert.Contains("settings_criteria", offered);
        Assert.DoesNotContain("admin_users", offered);
    }

    [Fact]
    public void AnAdminGetsEverything()
    {
        var offered = AiViewCatalog.For(canEdit: true, isAdmin: true);

        Assert.Equal(AiViewCatalog.Views.Count, offered.Count);
    }

    [Theory]
    [InlineData("admin_users", false, false)]
    [InlineData("admin_users", true, false)]
    [InlineData("settings_criteria", false, false)]
    public void IsAllowed_RefusesWhatTheRoleDoesNotCover(string id, bool canEdit, bool isAdmin)
    {
        var view = AiViewCatalog.Find(id)!;

        Assert.False(AiViewCatalog.IsAllowed(view, canEdit, isAdmin));
    }

    [Fact]
    public void Find_ReturnsNullForSomethingInvented()
    {
        Assert.Null(AiViewCatalog.Find("../../etc/passwd"));
        Assert.Null(AiViewCatalog.Find(null));
    }

    [Fact]
    public void TheToolOffersOnlyTheViewsTheRoleAllows()
    {
        var viewer = AiUiTools.DefinitionFor(canEdit: false, isAdmin: false);

        // The enum is the model's whole menu, so an absent id is never proposed.
        Assert.DoesNotContain("admin_users", viewer.InputSchemaJson);
        Assert.Contains("insights_conflicts", viewer.InputSchemaJson);
    }

    [Fact]
    public void TheToolSaysItChangesNothingInTheWorkspace()
    {
        // The read-only promise in the system prompt has to stay true with this tool in
        // hand: it moves the screen, not the data.
        var definition = AiUiTools.DefinitionFor(canEdit: true, isAdmin: true);

        Assert.Contains("changes nothing in the workspace", definition.Description);
    }
}
