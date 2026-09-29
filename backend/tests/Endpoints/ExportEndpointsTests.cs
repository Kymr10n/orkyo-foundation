using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Models;
using Api.Models.Export;
using Api.Models.Preset;
using Npgsql;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class ExportEndpointsTests
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly DatabaseFixture _fixture;
    private const string TenantSlug = TestConstants.TenantSlug;

    public ExportEndpointsTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _client = databaseFixture.Factory.CreateClient();
        _client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    private Task<string>? _token;
    private Task<string> Token => _token ??= DatabaseFixture.CreateMemberTokenAsync(RoleConstants.Admin);

    #region POST /api/admin/export

    [Fact]
    public async Task Export_DefaultRequest_ReturnsPayloadWithProvenance()
    {
        // Arrange
        var exportRequest = new ExportRequest();
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/export", await Token, exportRequest);

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

        var applyReq = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/apply", await Token, preset);
        var applyResponse = await _client.SendAsync(applyReq);
        Assert.Equal(HttpStatusCode.OK, applyResponse.StatusCode);

        // Act - export
        var exportRequest = new ExportRequest { IncludeMasterData = true, IncludePlanningData = false };
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/export", await Token, exportRequest);
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
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/export", await Token, exportRequest);

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

        var applyReq = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/apply", await Token, preset);
        await _client.SendAsync(applyReq);

        // Act - export twice
        var exportRequest = new ExportRequest { IncludeMasterData = true };
        var req1 = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/export", await Token, exportRequest);
        var resp1 = await _client.SendAsync(req1);
        var payload1 = await resp1.Content.ReadFromJsonAsync<ExportPayload>(_jsonOptions);

        var req2 = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/export", await Token, exportRequest);
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
        var createSiteReq = TestHelpers.AuthRequest(HttpMethod.Post, "/api/sites", await Token,
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
            var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/export", await Token, exportRequest);
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
    public async Task Export_PlacedRequest_ReportsTheLiveSpaceNotACancelledOne()
    {
        // The SQL excludes cancelled assignments, but the in-memory pick of "the" placement did not:
        // a cancelled assignment on another exported space was reported as the placement.
        var tag = $"exp-cx-{Guid.NewGuid():N}"[..20];
        await using var conn = new NpgsqlConnection(_fixture.TenantConnectionString);
        await conn.OpenAsync();
        try
        {
            var siteId = Guid.NewGuid();
            var oldSpaceId = Guid.NewGuid();
            var liveSpaceId = Guid.NewGuid();
            var requestId = Guid.NewGuid();
            await using (var seed = new NpgsqlCommand(@"
                INSERT INTO sites (id, name, code) VALUES (@siteId, @tag, @tag);
                INSERT INTO resources (id, resource_type_id, name, allocation_mode, base_availability_percent, is_active, home_site_id)
                SELECT @oldSpaceId, rt.id, @tag || ' old', 'Exclusive', 100, true, @siteId FROM resource_types rt WHERE rt.key = 'space';
                INSERT INTO resources (id, resource_type_id, name, allocation_mode, base_availability_percent, is_active, home_site_id)
                SELECT @liveSpaceId, rt.id, @tag || ' live', 'Exclusive', 100, true, @siteId FROM resource_types rt WHERE rt.key = 'space';
                INSERT INTO requests (id, name, site_id, status, start_ts, end_ts, minimal_duration_value, minimal_duration_unit,
                                      planning_mode, created_at, updated_at)
                VALUES (@requestId, @tag, @siteId, 'new', @start, @end, 60, 'minutes', 'leaf', NOW(), NOW());
                INSERT INTO resource_assignments (id, request_id, resource_id, start_utc, end_utc, assignment_status)
                VALUES (gen_random_uuid(), @requestId, @oldSpaceId, @start, @end, 'Cancelled'),
                       (gen_random_uuid(), @requestId, @liveSpaceId, @start, @end, 'Planned')", conn))
            {
                var start = new DateTime(2099, 3, 1, 9, 0, 0, DateTimeKind.Utc);
                seed.Parameters.AddWithValue("siteId", siteId);
                seed.Parameters.AddWithValue("tag", tag);
                seed.Parameters.AddWithValue("oldSpaceId", oldSpaceId);
                seed.Parameters.AddWithValue("liveSpaceId", liveSpaceId);
                seed.Parameters.AddWithValue("requestId", requestId);
                seed.Parameters.AddWithValue("start", start);
                seed.Parameters.AddWithValue("end", start.AddHours(1));
                await seed.ExecuteNonQueryAsync();
            }

            var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/export", await Token,
                new ExportRequest { SiteIds = [siteId], IncludeMasterData = true, IncludePlanningData = true });
            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<ExportPayload>(_jsonOptions);
            var exported = Assert.Single(payload!.Data.Requests!, r => r.Name == tag);
            Assert.Equal(tag + " live", exported.ResourceName);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand(@"
                DELETE FROM requests WHERE name = @tag;
                DELETE FROM resources WHERE name LIKE @tag || '%';
                DELETE FROM sites WHERE code = @tag", conn);
            cleanup.Parameters.AddWithValue("tag", tag);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Export_SeesSitesPastTheOldReadCap()
    {
        // The site read once stopped at 200 rows by name, so the 201st site was never exported.
        var prefix = $"zzz-s22-{Guid.NewGuid():N}"[..16];
        await using var conn = new NpgsqlConnection(_fixture.TenantConnectionString);
        await conn.OpenAsync();
        try
        {
            await using (var insert = new NpgsqlCommand(
                "INSERT INTO sites (name, code) SELECT @prefix || lpad(i::text, 3, '0'), @prefix || i "
                + "FROM generate_series(1, 201) AS i", conn))
            {
                insert.Parameters.AddWithValue("prefix", prefix);
                await insert.ExecuteNonQueryAsync();
            }
            Guid lastSiteId;
            await using (var last = new NpgsqlCommand("SELECT id FROM sites WHERE name = @name", conn))
            {
                last.Parameters.AddWithValue("name", prefix + "201");
                lastSiteId = (Guid)(await last.ExecuteScalarAsync())!;
            }

            var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/export", await Token,
                new ExportRequest { SiteIds = [lastSiteId], IncludeMasterData = true });
            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<ExportPayload>(_jsonOptions);
            Assert.Equal([lastSiteId], payload!.Provenance.SiteIds);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("DELETE FROM sites WHERE name LIKE @prefix || '%'", conn);
            cleanup.Parameters.AddWithValue("prefix", prefix);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Export_NonAdminUser_Returns403()
    {
        var token = await DatabaseFixture.CreateMemberTokenAsync("viewer");

        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/export", token);
        request.Content = JsonContent.Create(new ExportRequest(), options: _jsonOptions);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_MasterDataDisabled_ReturnsEmptyData()
    {
        // Arrange
        var exportRequest = new ExportRequest { IncludeMasterData = false, IncludePlanningData = false };
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/export", await Token, exportRequest);

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

    [Fact]
    public async Task Export_IncludesListDefinitionsWithColumnsAndSharedRows()
    {
        // Arrange — a definition with a column, a shared instance, and a row in it.
        var adminClient = _client;
        var createDefinition = TestHelpers.AuthRequest(HttpMethod.Post, "/api/list-definitions", await Token,
            new CreateListDefinitionRequest { Name = $"Components {Guid.NewGuid():N}" });
        var definitionResponse = await adminClient.SendAsync(createDefinition);
        Assert.Equal(HttpStatusCode.Created, definitionResponse.StatusCode);
        var definition = (await definitionResponse.Content.ReadFromJsonAsync<ListDefinitionInfo>(_jsonOptions))!;

        var createColumn = TestHelpers.AuthRequest(HttpMethod.Post,
            $"/api/list-definitions/{definition.Id}/columns", await Token,
            new CreateListColumnRequest { Key = "name", Label = "Name", DataType = ListColumnDataTypes.Text });
        Assert.Equal(HttpStatusCode.Created, (await adminClient.SendAsync(createColumn)).StatusCode);

        var createInstance = TestHelpers.AuthRequest(HttpMethod.Post,
            $"/api/list-definitions/{definition.Id}/instances", await Token,
            new CreateListInstanceRequest { Name = "Standard" });
        var instanceResponse = await adminClient.SendAsync(createInstance);
        Assert.Equal(HttpStatusCode.Created, instanceResponse.StatusCode);
        var instance = (await instanceResponse.Content.ReadFromJsonAsync<ListInstanceInfo>(_jsonOptions))!;

        var createRow = TestHelpers.AuthRequest(HttpMethod.Post,
            $"/api/list-instances/{instance.Id}/rows", await Token,
            new ListRowRequest
            {
                Values = new Dictionary<string, JsonElement>
                {
                    ["name"] = JsonDocument.Parse("\"Bolt\"").RootElement,
                },
            });
        var rowResponse = await adminClient.SendAsync(createRow);
        Assert.Equal(HttpStatusCode.Created, rowResponse.StatusCode);
        var row = (await rowResponse.Content.ReadFromJsonAsync<ListRowInfo>(_jsonOptions))!;

        // Act
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/export", await Token, new ExportRequest());
        var response = await adminClient.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ExportPayload>(_jsonOptions);

        var exported = Assert.Single(
            payload!.Data!.ListDefinitions!, d => d.Name == definition.Name);
        Assert.Equal("name", Assert.Single(exported.Columns).Key);

        var exportedInstance = Assert.Single(exported.SharedInstances);
        Assert.Equal("Standard", exportedInstance.Name);

        var exportedRow = Assert.Single(exportedInstance.Rows);
        Assert.Equal("Bolt", exportedRow.Values["name"].GetString());
        // The row id travels because lookup values reference rows by id: without it an exported
        // selection would name something the export does not contain.
        Assert.Equal(row.Id, exportedRow.Id);
    }
}
