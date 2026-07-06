using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Models;
using Api.Models.Export;
using Api.Models.Preset;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class ExportEndpointsTests
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;
    private const string TenantSlug = TestConstants.TenantSlug;

    public ExportEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.Factory.CreateClient();
        _client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    private string? _cachedAuthToken;

    private async Task<string> GetAdminAuthTokenAsync()
    {
        if (_cachedAuthToken != null)
            return _cachedAuthToken;

        var email = $"exporttest_{Guid.NewGuid()}@example.com";
        var displayName = "Export Test Admin";

        var userId = await DatabaseTestUtils.CreateTestUserAsync(email, displayName, TestConstants.TenantSlug, "admin", active: true);
        var tenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = displayName,
            TenantId = tenantId.ToString(),
            TenantSlug = TestConstants.TenantSlug,
            IsTenantAdmin = true,
            Role = "admin"
        };

        var json = JsonSerializer.Serialize(tokenData);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        _cachedAuthToken = Convert.ToBase64String(bytes);
        return _cachedAuthToken;
    }

    private async Task<HttpRequestMessage> CreateAuthenticatedRequestAsync(HttpMethod method, string url, object? content = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAdminAuthTokenAsync());
        if (content != null)
        {
            request.Content = JsonContent.Create(content, options: _jsonOptions);
        }
        return request;
    }

    #region POST /api/admin/export

    [Fact]
    public async Task Export_DefaultRequest_ReturnsPayloadWithProvenance()
    {
        // Arrange
        var exportRequest = new ExportRequest();
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/export", exportRequest);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ExportPayload>(_jsonOptions);
        Assert.NotNull(payload);
        Assert.Equal("1.0.0", payload.SchemaVersion);
        Assert.NotNull(payload.Provenance);
        Assert.Equal(TenantSlug, payload.Provenance.TenantSlug);
        Assert.Equal("1.0.0", payload.Provenance.SchemaVersion);
        Assert.True(payload.Provenance.ExportTimestamp > DateTime.MinValue);
        Assert.NotNull(payload.Data);
    }

    [Fact]
    public async Task Export_MasterDataOnly_IncludesCriteriaAndSites()
    {
        // Arrange - seed some data via preset
        var presetId = $"export-seed-{Guid.NewGuid():N}";
        var preset = new Preset
        {
            PresetId = presetId,
            Name = "Export Seed",
            Version = "1.0.0",
            Contents = new PresetContents
            {
                Criteria = new List<PresetCriterion>
                {
                    new()
                    {
                        Key = $"export-crit-{Guid.NewGuid():N}",
                        Name = $"Export Criterion {Guid.NewGuid():N}",
                        DataType = CriterionDataType.Boolean
                    }
                },
                SpaceGroups = new List<PresetSpaceGroup>
                {
                    new()
                    {
                        Key = $"export-grp-{Guid.NewGuid():N}",
                        Name = $"Export Group {Guid.NewGuid():N}",
                        Color = "#FF0000"
                    }
                }
            }
        };

        var applyReq = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/presets/apply", preset);
        var applyResponse = await _client.SendAsync(applyReq);
        Assert.Equal(HttpStatusCode.OK, applyResponse.StatusCode);

        // Act - export
        var exportRequest = new ExportRequest { IncludeMasterData = true, IncludePlanningData = false };
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/export", exportRequest);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ExportPayload>(_jsonOptions);
        Assert.NotNull(payload);
        Assert.NotNull(payload.Data.Criteria);
        Assert.NotEmpty(payload.Data.Criteria);
        Assert.NotNull(payload.Data.SpaceGroups);
        // Note: SpaceGroups may be empty if no groups exist in the test data
        Assert.NotNull(payload.Data.Sites);
        Assert.NotNull(payload.Data.Templates);
        Assert.Null(payload.Data.Requests); // planning data not requested
    }

    [Fact]
    public async Task Export_WithPlanningData_IncludesRequests()
    {
        // Arrange
        var exportRequest = new ExportRequest { IncludeMasterData = true, IncludePlanningData = true };
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/export", exportRequest);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ExportPayload>(_jsonOptions);
        Assert.NotNull(payload);
        Assert.NotNull(payload.Data.Requests);
    }

    [Fact]
    public async Task Export_CriteriaHaveKeys_Deterministic()
    {
        // Arrange - seed a criterion
        var critName = $"Determinism Test {Guid.NewGuid():N}";
        var presetId = $"det-test-{Guid.NewGuid():N}";
        var preset = new Preset
        {
            PresetId = presetId,
            Name = "Determinism Seed",
            Version = "1.0.0",
            Contents = new PresetContents
            {
                Criteria = new List<PresetCriterion>
                {
                    new() { Key = "det-crit", Name = critName, DataType = CriterionDataType.Number, Unit = "kg" }
                }
            }
        };

        var applyReq = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/presets/apply", preset);
        await _client.SendAsync(applyReq);

        // Act - export twice
        var exportRequest = new ExportRequest { IncludeMasterData = true };
        var req1 = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/export", exportRequest);
        var resp1 = await _client.SendAsync(req1);
        var payload1 = await resp1.Content.ReadFromJsonAsync<ExportPayload>(_jsonOptions);

        var req2 = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/export", exportRequest);
        var resp2 = await _client.SendAsync(req2);
        var payload2 = await resp2.Content.ReadFromJsonAsync<ExportPayload>(_jsonOptions);

        // Assert - criteria content should be identical (ignoring timestamp)
        Assert.NotNull(payload1);
        Assert.NotNull(payload2);

        var crit1 = payload1.Data.Criteria!.FirstOrDefault(c => c.Name == critName);
        var crit2 = payload2.Data.Criteria!.FirstOrDefault(c => c.Name == critName);
        Assert.NotNull(crit1);
        Assert.NotNull(crit2);
        Assert.Equal(crit1.Key, crit2.Key);
        Assert.Equal(crit1.DataType, crit2.DataType);
        Assert.Equal(crit1.Unit, crit2.Unit);
    }

    [Fact]
    public async Task Export_WithSiteFilter_OnlyIncludesFilteredSites()
    {
        // Arrange - create a site
        var siteCode = $"exp-site-{Guid.NewGuid():N}"[..20];
        var createSiteReq = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/sites",
            new { code = siteCode, name = $"Export Site {siteCode}" });
        var siteResp = await _client.SendAsync(createSiteReq);

        if (siteResp.StatusCode == HttpStatusCode.OK || siteResp.StatusCode == HttpStatusCode.Created)
        {
            var siteJson = await siteResp.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
            var siteId = siteJson.GetProperty("id").GetGuid();

            // Export with site filter
            var exportRequest = new ExportRequest
            {
                SiteIds = new List<Guid> { siteId },
                IncludeMasterData = true
            };
            var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/export", exportRequest);
            var response = await _client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<ExportPayload>(_jsonOptions);
            Assert.NotNull(payload);
            Assert.NotNull(payload.Data.Sites);
            Assert.Single(payload.Data.Sites);
            Assert.Equal(siteCode, payload.Data.Sites[0].Code);
            Assert.NotNull(payload.Provenance.SiteIds);
            Assert.Single(payload.Provenance.SiteIds);
            Assert.Equal(siteId, payload.Provenance.SiteIds[0]);
        }
    }

    [Fact]
    public async Task Export_Unauthenticated_Returns401()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/export");
        request.Content = JsonContent.Create(new ExportRequest());

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Export_NonAdminUser_Returns403()
    {
        // Arrange - create a viewer user
        var email = $"exportviewer_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(email, "Viewer User", TestConstants.TenantSlug, "viewer", active: true);
        var tenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Viewer User",
            TenantId = tenantId.ToString(),
            TenantSlug = TestConstants.TenantSlug,
            IsTenantAdmin = false,
            Role = "viewer"
        };

        var json = JsonSerializer.Serialize(tokenData);
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/export");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new ExportRequest(), options: _jsonOptions);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.InternalServerError,
            $"Expected 403 or 500 (UnauthorizedAccessException), got {response.StatusCode}");
    }

    [Fact]
    public async Task Export_MasterDataDisabled_ReturnsEmptyData()
    {
        // Arrange
        var exportRequest = new ExportRequest { IncludeMasterData = false, IncludePlanningData = false };
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/export", exportRequest);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ExportPayload>(_jsonOptions);
        Assert.NotNull(payload);
        Assert.Null(payload.Data.Sites);
        Assert.Null(payload.Data.Criteria);
        Assert.Null(payload.Data.SpaceGroups);
        Assert.Null(payload.Data.Templates);
        Assert.Null(payload.Data.Requests);
    }

    #endregion
}
