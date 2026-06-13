using System.Net;
using System.Net.Http.Json;
using Api.Constants;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Authorization boundary tests for the settings-page surfaces (Criteria, Templates,
/// Presets, Scheduling). Settings content is readable by any tenant member but only
/// writable by Editor+ — these tests lock that contract so the role gate can't silently
/// regress (e.g. the criterion-applicability PUT that was previously admin-only).
/// </summary>
[Collection("Database collection")]
public class SettingsAuthorizationTests
{
    private readonly DatabaseFixture _fixture;
    private readonly HttpClient _admin;

    public SettingsAuthorizationTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _admin = databaseFixture.CreateAuthorizedClient();
    }

    private async Task<CriterionInfo> CreateCriterionAsAdminAsync()
    {
        var createRequest = new CreateCriterionRequest
        {
            Name = $"test_authz_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
        };
        var response = await _admin.PostAsJsonAsync("/api/criteria", createRequest);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CriterionInfo>())!;
    }

    [Fact]
    public async Task UpdateCriterionApplicability_AsEditor_ReturnsOk()
    {
        // Regression guard: this PUT was previously gated by RequireAdminAccess, which
        // 403'd editors saving a criterion's resource-type scope from the settings dialog.
        var criterion = await CreateCriterionAsAdminAsync();
        var editor = _fixture.CreateClientWithRole(RoleConstants.Editor);

        var response = await editor.PutAsJsonAsync(
            $"/api/criteria/{criterion.Id}/applicability",
            new UpdateCriterionApplicabilityRequest { ResourceTypeKeys = new List<string> { "space" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCriterionApplicability_AsViewer_ReturnsForbidden()
    {
        var criterion = await CreateCriterionAsAdminAsync();
        var viewer = _fixture.CreateClientWithRole(RoleConstants.Viewer);

        var response = await viewer.PutAsJsonAsync(
            $"/api/criteria/{criterion.Id}/applicability",
            new UpdateCriterionApplicabilityRequest { ApplicableToRequests = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateCriterion_AsViewer_ReturnsForbidden()
    {
        var viewer = _fixture.CreateClientWithRole(RoleConstants.Viewer);

        var response = await viewer.PostAsJsonAsync("/api/criteria", new CreateCriterionRequest
        {
            Name = $"test_authz_viewer_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCriteria_AsViewer_ReturnsOk()
    {
        // Reads stay open to any tenant member, including viewers.
        var viewer = _fixture.CreateClientWithRole(RoleConstants.Viewer);

        var response = await viewer.GetAsync("/api/criteria");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateTemplate_AsViewer_ReturnsForbidden()
    {
        var viewer = _fixture.CreateClientWithRole(RoleConstants.Viewer);

        var response = await viewer.PostAsJsonAsync("/api/templates", new CreateTemplateRequest
        {
            Name = $"test_authz_tpl_{Guid.NewGuid():N}",
            EntityType = TemplateEntityTypes.Space
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ApplyPreset_AsViewer_ReturnsForbidden()
    {
        var viewer = _fixture.CreateClientWithRole(RoleConstants.Viewer);

        // A well-formed body so binding succeeds and the request reaches the role gate
        // (a malformed body would 400 before authorization runs).
        var response = await viewer.PostAsJsonAsync("/api/admin/presets/apply",
            new { presetId = "x", name = "x", version = "1.0.0", contents = new { } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpsertSchedulingSettings_AsViewer_ReturnsForbidden()
    {
        var viewer = _fixture.CreateClientWithRole(RoleConstants.Viewer);

        var response = await viewer.PutAsJsonAsync(
            $"/api/sites/{Guid.NewGuid()}/scheduling/",
            new UpsertSchedulingSettingsRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
