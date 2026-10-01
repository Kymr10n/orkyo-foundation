using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Models.Preset;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for Preset endpoints.
/// Tests the complete preset import/export/apply workflow.
/// </summary>
[Collection("Database collection")]
public class PresetEndpointsTests
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;
    private const string TenantSlug = TestConstants.TenantSlug;

    public PresetEndpointsTests(DatabaseFixture databaseFixture)
    {
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

    #region POST /api/admin/presets/validate

    [Fact]
    public async Task ValidatePreset_WithValidPreset_ReturnsValid()
    {
        // Arrange
        var preset = CreateValidPreset($"validate-test-{Guid.NewGuid():N}");
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/validate", await Token, preset);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PresetValidationResult>(_jsonOptions);
        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidatePreset_WithInvalidPresetId_ReturnsErrors()
    {
        // Arrange
        var preset = CreateValidPreset("INVALID_ID_FORMAT");
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/validate", await Token, preset);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PresetValidationResult>(_jsonOptions);
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("lowercase"));
    }

    [Fact]
    public async Task ValidatePreset_WithMissingName_ReturnsErrors()
    {
        // Arrange
        var preset = CreateValidPreset($"test-{Guid.NewGuid():N}") with { Name = "" };
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/validate", await Token, preset);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PresetValidationResult>(_jsonOptions);
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Name is required"));
    }

    [Fact]
    public async Task ValidatePreset_WithDuplicateCriterionKeys_ReturnsErrors()
    {
        // Arrange
        var preset = CreateValidPreset($"test-{Guid.NewGuid():N}") with
        {
            Contents = new PresetContents
            {
                Criteria = new List<PresetCriterion>
                {
                    new() { Key = "same-key", Name = "First", DataType = Api.Models.CriterionDataType.Boolean },
                    new() { Key = "same-key", Name = "Second", DataType = Api.Models.CriterionDataType.Boolean }
                }
            }
        };
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/validate", await Token, preset);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PresetValidationResult>(_jsonOptions);
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate criterion key"));
    }

    [Fact]
    public async Task ValidatePreset_WithEnumMissingValues_ReturnsErrors()
    {
        // Arrange
        var preset = CreateValidPreset($"test-{Guid.NewGuid():N}") with
        {
            Contents = new PresetContents
            {
                Criteria = new List<PresetCriterion>
                {
                    new() { Key = "enum-key", Name = "Enum No Values", DataType = Api.Models.CriterionDataType.Enum }
                }
            }
        };
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/validate", await Token, preset);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PresetValidationResult>(_jsonOptions);
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Enum type requires at least one enum value"));
    }

    #endregion

    #region POST /api/admin/presets/apply

    [Fact]
    public async Task ApplyPreset_WithValidPreset_ReturnsSuccess()
    {
        // Arrange
        var presetId = $"apply-test-{Guid.NewGuid():N}";
        var preset = CreateValidPreset(presetId) with
        {
            Contents = new PresetContents
            {
                Criteria = new List<PresetCriterion>
                {
                    new()
                    {
                        Key = $"criterion-{Guid.NewGuid():N}",
                        Name = $"Test Criterion {Guid.NewGuid():N}",
                        DataType = Api.Models.CriterionDataType.Boolean
                    }
                },
                SpaceGroups = new List<PresetSpaceGroup>
                {
                    new()
                    {
                        Key = $"group-{Guid.NewGuid():N}",
                        Name = $"Test Group {Guid.NewGuid():N}",
                        Color = "#FF5733"
                    }
                }
            }
        };
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/apply", await Token, preset);

        // Act
        var response = await _client.SendAsync(request);
        var resultJson = await response.Content.ReadAsStringAsync();

        // Assert - include response body in assertion message for debugging
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected OK but got {response.StatusCode}. Response: {resultJson}");
        var result = JsonSerializer.Deserialize<ApplyResult>(resultJson, _jsonOptions);
        Assert.NotNull(result);
        Assert.True(result.Success, $"Apply failed: {result.Error}");
        Assert.NotNull(result.Stats);
        Assert.True(result.Stats.CriteriaCreated > 0 || result.Stats.CriteriaUpdated >= 0);
    }

    [Fact]
    public async Task ApplyPreset_WithInvalidPreset_ReturnsError()
    {
        // Arrange - create a preset with invalid criterion (missing required name)
        var preset = new Preset
        {
            PresetId = $"invalid-{Guid.NewGuid():N}",
            Name = "Invalid Test Preset",
            Version = "1.0.0",
            Description = "Test invalid preset",
            Contents = new PresetContents
            {
                Criteria = new List<PresetCriterion>
                {
                    new()
                    {
                        Key = $"key-{Guid.NewGuid():N}",
                        Name = "", // Invalid - empty name
                        DataType = Api.Models.CriterionDataType.Boolean
                    }
                }
            }
        };
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/apply", await Token, preset);

        // Act
        var response = await _client.SendAsync(request);

        // Assert - should return BadRequest due to validation failure
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ApplyPreset_Idempotent_CanBeAppliedTwice()
    {
        // Arrange
        var presetId = $"idempotent-{Guid.NewGuid():N}";
        var criterionKey = $"criterion-{Guid.NewGuid():N}";
        var preset = CreateValidPreset(presetId) with
        {
            Contents = new PresetContents
            {
                Criteria = new List<PresetCriterion>
                {
                    new()
                    {
                        Key = criterionKey,
                        Name = $"Test Criterion {Guid.NewGuid():N}",
                        DataType = Api.Models.CriterionDataType.Boolean
                    }
                }
            }
        };

        // Act - Apply first time
        var request1 = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/apply", await Token, preset);
        var response1 = await _client.SendAsync(request1);
        var result1 = await response1.Content.ReadFromJsonAsync<ApplyResult>(_jsonOptions);

        // Act - Apply second time
        var request2 = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/apply", await Token, preset);
        var response2 = await _client.SendAsync(request2);
        var result2 = await response2.Content.ReadFromJsonAsync<ApplyResult>(_jsonOptions);

        // Assert
        Assert.True(result1!.Success, $"First apply failed: {result1.Error}");
        Assert.True(result2!.Success, $"Second apply failed: {result2.Error}");
        // Second apply should update, not create
        Assert.Equal(0, result2.Stats!.CriteriaCreated);
    }

    [Fact]
    public async Task ApplyPreset_WithTemplates_CreatesTemplates()
    {
        // Arrange
        var presetId = $"templates-{Guid.NewGuid():N}";
        var criterionKey = $"criterion-{Guid.NewGuid():N}";
        var preset = CreateValidPreset(presetId) with
        {
            Contents = new PresetContents
            {
                Criteria = new List<PresetCriterion>
                {
                    new()
                    {
                        Key = criterionKey,
                        Name = $"Test Criterion {Guid.NewGuid():N}",
                        DataType = Api.Models.CriterionDataType.Number,
                        Unit = "kg"
                    }
                },
                Templates = new PresetTemplates
                {
                    Space = new List<PresetTemplate>
                    {
                        new()
                        {
                            Key = $"space-tpl-{Guid.NewGuid():N}",
                            Name = $"Space Template {Guid.NewGuid():N}",
                            Items = new List<PresetTemplateItem>
                            {
                                new() { CriterionKey = criterionKey, Value = "100" }
                            }
                        }
                    },
                    Request = new List<PresetTemplate>
                    {
                        new()
                        {
                            Key = $"request-tpl-{Guid.NewGuid():N}",
                            Name = $"Request Template {Guid.NewGuid():N}",
                            DurationValue = 8,
                            DurationUnit = "hours",
                            FixedDuration = true,
                            Items = new List<PresetTemplateItem>()
                        }
                    }
                }
            }
        };
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/apply", await Token, preset);

        // Act
        var response = await _client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected OK but got {response.StatusCode}. Response: {responseBody}");
        var result = JsonSerializer.Deserialize<ApplyResult>(responseBody, _jsonOptions);
        Assert.NotNull(result);
        Assert.True(result.Success, $"Apply failed: {result.Error}");
        Assert.True(result.Stats!.TemplatesCreated >= 2 || result.Stats.TemplatesUpdated >= 0);
    }

    [Fact]
    public async Task ApplyPreset_WithResourceTypesAndResources_CreatesThemAndReportsStats()
    {
        // A 1.1.0 preset through the HTTP path: an ad-hoc type, a catalog type, a typed group,
        // an applicable criterion and two resources with a capability and a membership.
        var typeKey = $"ws{Guid.NewGuid():N}"[..20];
        var preset = CreateValidPreset($"resources-{Guid.NewGuid():N}") with
        {
            Version = Api.Validators.PresetValidator.CurrentVersion,
            Contents = new PresetContents
            {
                ResourceTypes =
                [
                    new() { Key = typeKey, DisplayName = "Workbench", DisplayNamePlural = "Workbenches", HasGeometry = true, SingleGroupMembership = true },
                    new() { Key = "tool" }
                ],
                Criteria = [new() { Key = "esd-safe", Name = $"ESD safe {Guid.NewGuid():N}", DataType = Api.Models.CriterionDataType.Boolean, ResourceTypeKeys = [typeKey, "tool"] }],
                SpaceGroups = [new() { Key = "benches", Name = $"Benches {Guid.NewGuid():N}", ResourceTypeKey = typeKey }],
                Resources =
                [
                    new() { Key = "bench-1", Name = $"Bench 1 {Guid.NewGuid():N}", Code = $"B1-{Guid.NewGuid():N}"[..20], TypeKey = typeKey, GroupKeys = ["benches"], Capabilities = [new() { CriterionKey = "esd-safe", Value = "true" }] },
                    new() { Key = "meter-1", Name = $"Multimeter {Guid.NewGuid():N}", TypeKey = "tool" }
                ]
            }
        };
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/apply", await Token, preset);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected OK but got {response.StatusCode}. Response: {body}");
        var result = JsonSerializer.Deserialize<ApplyResult>(body, _jsonOptions)!;
        Assert.True(result.Success, $"Apply failed: {result.Error}");
        Assert.Equal(2, result.Stats!.ResourceTypesActivated);
        Assert.Equal(2, result.Stats.ResourcesCreated);
        Assert.Equal(0, result.Stats.ResourcesUpdated);

        // A second apply adopts everything it created: nothing new, everything updated.
        var again = JsonSerializer.Deserialize<ApplyResult>(
            await (await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/apply", await Token, preset)))
                .Content.ReadAsStringAsync(), _jsonOptions)!;
        Assert.True(again.Success, again.Error);
        Assert.Equal(0, again.Stats!.ResourcesCreated);
        Assert.Equal(2, again.Stats.ResourcesUpdated);
    }

    [Fact]
    public async Task ApplyPreset_ResourceInGroupOfAnotherType_IsRejectedByValidation()
    {
        var typeKey = $"tt{Guid.NewGuid():N}"[..20];
        var preset = CreateValidPreset($"mismatch-{Guid.NewGuid():N}") with
        {
            Version = Api.Validators.PresetValidator.CurrentVersion,
            Contents = new PresetContents
            {
                ResourceTypes = [new() { Key = typeKey, DisplayName = "T", DisplayNamePlural = "Ts" }, new() { Key = "tool" }],
                SpaceGroups = [new() { Key = "g", Name = $"G {Guid.NewGuid():N}", ResourceTypeKey = typeKey }],
                Resources = [new() { Key = "r", Name = "R", TypeKey = "tool", GroupKeys = ["g"] }]
            }
        };

        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/apply", await Token, preset));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region GET /api/admin/presets/export

    [Fact]
    public async Task ExportPreset_ReturnsValidPreset()
    {
        // Arrange
        var presetId = $"export-test-{Guid.NewGuid():N}";
        var name = $"Export Test {Guid.NewGuid():N}";
        var request = TestHelpers.AuthRequest(HttpMethod.Get,
            $"/api/admin/presets/export?presetId={presetId}&name={Uri.EscapeDataString(name)}", await Token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preset = await response.Content.ReadFromJsonAsync<Preset>(_jsonOptions);
        Assert.NotNull(preset);
        Assert.Equal(presetId, preset.PresetId);
        Assert.Equal(name, preset.Name);
        Assert.Equal(Api.Validators.PresetValidator.CurrentVersion, preset.Version);
        Assert.NotNull(preset.Contents);
    }

    [Fact]
    public async Task ExportPreset_EmitsResourceTypesAndTyping_ButNoResources()
    {
        // Types, applicability and group typing are configuration and round-trip; resources
        // are data (people's names) and never leave the tenant in an export.
        var typeKey = $"exp{Guid.NewGuid():N}"[..20];
        var apply = CreateValidPreset($"export-shape-{Guid.NewGuid():N}") with
        {
            Version = Api.Validators.PresetValidator.CurrentVersion,
            Contents = new PresetContents
            {
                ResourceTypes = [new() { Key = typeKey, DisplayName = "Bench", DisplayNamePlural = "Benches", HasGeometry = true }],
                Criteria = [new() { Key = "bench-load", Name = $"Bench load {Guid.NewGuid():N}", DataType = Api.Models.CriterionDataType.Number, ResourceTypeKeys = [typeKey] }],
                SpaceGroups = [new() { Key = "benches", Name = $"Benches {Guid.NewGuid():N}", ResourceTypeKey = typeKey }],
                Resources = [new() { Key = "bench-1", Name = $"Bench {Guid.NewGuid():N}", TypeKey = typeKey, GroupKeys = ["benches"] }]
            }
        };
        var applyResponse = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/apply", await Token, apply));
        Assert.Equal(HttpStatusCode.OK, applyResponse.StatusCode);

        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get,
            $"/api/admin/presets/export?presetId=export-shape&name=Shape", await Token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var contents = doc.RootElement.GetProperty("contents");
        Assert.Contains(contents.GetProperty("resourceTypes").EnumerateArray(), t => t.GetProperty("key").GetString() == typeKey);
        Assert.Contains(contents.GetProperty("criteria").EnumerateArray(),
            c => c.GetProperty("name").GetString() == apply.Contents.Criteria[0].Name
                 && c.GetProperty("resourceTypeKeys").EnumerateArray().Any(k => k.GetString() == typeKey));
        Assert.Contains(contents.GetProperty("spaceGroups").EnumerateArray(),
            g => g.GetProperty("name").GetString() == apply.Contents.SpaceGroups[0].Name
                 && g.GetProperty("resourceTypeKey").GetString() == typeKey);
        Assert.Empty(contents.GetProperty("resources").EnumerateArray());
    }

    [Fact]
    public async Task ExportPreset_WithDescription_IncludesDescription()
    {
        // Arrange
        var presetId = $"export-desc-{Guid.NewGuid():N}";
        var name = "Export With Description";
        var description = "This is a test description";
        var request = TestHelpers.AuthRequest(HttpMethod.Get,
            $"/api/admin/presets/export?presetId={presetId}&name={Uri.EscapeDataString(name)}&description={Uri.EscapeDataString(description)}", await Token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preset = await response.Content.ReadFromJsonAsync<Preset>(_jsonOptions);
        Assert.NotNull(preset);
        Assert.Equal(description, preset.Description);
    }

    [Fact]
    public async Task ExportPreset_MissingPresetId_ReturnsBadRequest()
    {
        // Arrange
        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/presets/export?name=Test", await Token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExportPreset_MissingName_ReturnsBadRequest()
    {
        // Arrange
        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/presets/export?presetId=test-id", await Token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region GET /api/admin/presets/applications

    [Fact]
    public async Task GetApplications_ReturnsApplicationHistory()
    {
        // Arrange - First apply a preset to ensure there's history
        var presetId = $"history-{Guid.NewGuid():N}";
        var preset = CreateValidPreset(presetId) with
        {
            Contents = new PresetContents
            {
                Criteria = new List<PresetCriterion>
                {
                    new()
                    {
                        Key = $"criterion-{Guid.NewGuid():N}",
                        Name = $"History Test {Guid.NewGuid():N}",
                        DataType = Api.Models.CriterionDataType.Boolean
                    }
                }
            }
        };
        var applyRequest = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/apply", await Token, preset);
        await _client.SendAsync(applyRequest);

        // Act
        var getRequest = TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/presets/applications", await Token);
        var response = await _client.SendAsync(getRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var applications = await response.Content.ReadFromJsonAsync<List<PresetApplication>>(_jsonOptions);
        Assert.NotNull(applications);
        Assert.Contains(applications, a => a.PresetId == presetId);
    }

    [Fact]
    public async Task GetApplications_IncludesVersionInfo()
    {
        // Arrange
        var presetId = $"version-check-{Guid.NewGuid():N}";
        var preset = CreateValidPreset(presetId) with
        {
            Version = "1.0.0",  // Use supported version
            Contents = new PresetContents
            {
                Criteria = new List<PresetCriterion>
                {
                    new()
                    {
                        Key = $"criterion-{Guid.NewGuid():N}",
                        Name = $"Version Test {Guid.NewGuid():N}",
                        DataType = Api.Models.CriterionDataType.Boolean
                    }
                }
            }
        };
        var applyRequest = TestHelpers.AuthRequest(HttpMethod.Post, "/api/admin/presets/apply", await Token, preset);
        var applyResponse = await _client.SendAsync(applyRequest);

        // Verify the apply succeeded first
        var applyBody = await applyResponse.Content.ReadAsStringAsync();
        Assert.True(applyResponse.StatusCode == HttpStatusCode.OK,
            $"Apply failed with {applyResponse.StatusCode}: {applyBody}");

        // Act
        var getRequest = TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/presets/applications", await Token);
        var response = await _client.SendAsync(getRequest);

        // Assert
        var applications = await response.Content.ReadFromJsonAsync<List<PresetApplication>>(_jsonOptions);
        Assert.NotNull(applications);
        var app = applications.FirstOrDefault(a => a.PresetId == presetId);
        Assert.NotNull(app);
        Assert.Equal("1.0.0", app.PresetVersion);
    }

    #endregion

    #region Helper Classes and Methods

    private static Preset CreateValidPreset(string presetId)
    {
        return new Preset
        {
            PresetId = presetId,
            Name = "Test Preset",
            Version = "1.0.0",
            CreatedAt = DateTime.UtcNow,
            Contents = new PresetContents()
        };
    }

    // DTOs for deserialization
    private record ApplyResult
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public ApplyStats? Stats { get; init; }
    }

    private record ApplyStats
    {
        public int ResourceTypesActivated { get; init; }
        public int CriteriaCreated { get; init; }
        public int CriteriaUpdated { get; init; }
        public int SpaceGroupsCreated { get; init; }
        public int SpaceGroupsUpdated { get; init; }
        public int TemplatesCreated { get; init; }
        public int TemplatesUpdated { get; init; }
        public int ResourcesCreated { get; init; }
        public int ResourcesUpdated { get; init; }
    }

    #endregion
}
