using System.Net;
using System.Net.Http.Json;
using Api.Constants;
using Xunit;

namespace Orkyo.Foundation.Tests.Authorization;

/// <summary>
/// Behavioural matrix locking the three-tier role contract (see docs/authorization.md):
/// Viewer reads everything but writes nothing; Editor writes general content but not the
/// Administration area; Admin does everything. Site create/edit/delete is Admin-only while the
/// site list stays member-readable.
///
/// Allowed cases assert "not 403" (the request may still 400/404 on body/state — we only care that
/// the authorization gate let it through); denied cases assert exactly 403.
/// </summary>
[Collection("Database collection")]
public class AuthorizationMatrixTests
{
    private readonly HttpClient _viewer;
    private readonly HttpClient _editor;
    private readonly HttpClient _admin;

    public AuthorizationMatrixTests(DatabaseFixture fixture)
    {
        _viewer = fixture.CreateClientWithRole(RoleConstants.Viewer);
        _editor = fixture.CreateClientWithRole(RoleConstants.Editor);
        _admin = fixture.CreateClientWithRole(RoleConstants.Admin);
    }

    private static void AssertForbidden(HttpResponseMessage r) =>
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);

    private static void AssertNotForbidden(HttpResponseMessage r) =>
        Assert.NotEqual(HttpStatusCode.Forbidden, r.StatusCode);

    // ── General content: read = member, write = Editor+ ───────────────────────

    [Fact]
    public async Task GeneralRead_AsViewer_IsAllowed() =>
        AssertNotForbidden(await _viewer.GetAsync("/api/resources"));

    // DELETE /{guid} binds trivially, so the authorization filter always runs (a body POST can 400
    // on binding before the gate). Editor "allowed" surfaces as 404 (no such resource) — not 403.
    [Fact]
    public async Task GeneralWrite_AsViewer_IsForbidden() =>
        AssertForbidden(await _viewer.DeleteAsync($"/api/resources/{Guid.NewGuid()}"));

    [Fact]
    public async Task GeneralWrite_AsEditor_IsAllowed() =>
        AssertNotForbidden(await _editor.DeleteAsync($"/api/resources/{Guid.NewGuid()}"));

    [Fact]
    public async Task NonMutatingValidatePost_AsViewer_IsAllowed() =>
        // /validate is marked AllowMemberWrite — it computes but does not persist.
        AssertNotForbidden(await _viewer.PostAsJsonAsync("/api/resource-assignments/validate", new { }));

    // ── Administration area: Admin only, for reads and writes ──────────────────

    [Fact]
    public async Task AdminAreaRead_AsEditor_IsForbidden() =>
        AssertForbidden(await _editor.GetAsync("/api/settings"));

    [Fact]
    public async Task AdminAreaRead_AsViewer_IsForbidden() =>
        AssertForbidden(await _viewer.GetAsync("/api/settings"));

    [Fact]
    public async Task AdminAreaRead_AsAdmin_IsAllowed() =>
        AssertNotForbidden(await _admin.GetAsync("/api/settings"));

    [Fact]
    public async Task AdminAreaWrite_AsEditor_IsForbidden() =>
        AssertForbidden(await _editor.DeleteAsync($"/api/users/{Guid.NewGuid()}"));

    [Fact]
    public async Task AdminAreaWrite_AsAdmin_IsAllowed() =>
        AssertNotForbidden(await _admin.DeleteAsync($"/api/users/{Guid.NewGuid()}"));

    // ── Sites: read = member, write = Admin ───────────────────────────────────

    [Fact]
    public async Task SiteRead_AsViewer_IsAllowed() =>
        AssertNotForbidden(await _viewer.GetAsync("/api/sites"));

    [Fact]
    public async Task SiteWrite_AsEditor_IsForbidden() =>
        AssertForbidden(await _editor.DeleteAsync($"/api/sites/{Guid.NewGuid()}"));

    [Fact]
    public async Task SiteWrite_AsAdmin_IsAllowed() =>
        AssertNotForbidden(await _admin.DeleteAsync($"/api/sites/{Guid.NewGuid()}"));
}
