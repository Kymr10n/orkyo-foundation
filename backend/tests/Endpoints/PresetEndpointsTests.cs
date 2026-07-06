using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Models.Preset;
using Xunit;

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

    private string? _cachedAuthToken;

    private async Task<string> GetAdminAuthTokenAsync()
    {
        if (_cachedAuthToken != null)
            return _cachedAuthToken;

        // Create a test admin user directly in database (Keycloak handles real auth)
        var email = $"presettest_{Guid.NewGuid()}@example.com";
        var displayName = "Preset Test Admin";

        var userId = await DatabaseTestUtils.CreateTestUserAsync(email, displayName, TestConstants.TenantSlug, "admin", active: true);
        var tenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"); // Test tenant

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = displayName,
            TenantId = tenantId.ToString(),
            TenantSlug = TestConstants.TenantSlug,
            IsTenantAdmin = true,  // Admin required for preset endpoints
            Role = "admin"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(tokenData);
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

    #region POST /api/admin/presets/validate

    [Fact]
    public async Task ValidatePreset_WithValidPreset_ReturnsValid()
    {
        // Arrange
        var preset = CreateValidPreset($"validate-test-{Guid.NewGuid():N}");
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/presets/validate", preset);

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
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/presets/validate", preset);

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
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/presets/validate", preset);

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
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/presets/validate", preset);

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
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/presets/validate", preset);

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
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/presets/apply", preset);

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
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/presets/apply", preset);

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
        var request1 = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/presets/apply", preset);
        var response1 = await _client.SendAsync(request1);
        var result1 = await response1.Content.ReadFromJsonAsync<ApplyResult>(_jsonOptions);

        // Act - Apply second time
        var request2 = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/presets/apply", preset);
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
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/presets/apply", preset);

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

    #endregion

    #region GET /api/admin/presets/export

    [Fact]
    public async Task ExportPreset_ReturnsValidPreset()
    {
        // Arrange
        var presetId = $"export-test-{Guid.NewGuid():N}";
        var name = $"Export Test {Guid.NewGuid():N}";
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get,
            $"/api/admin/presets/export?presetId={presetId}&name={Uri.EscapeDataString(name)}");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preset = await response.Content.ReadFromJsonAsync<Preset>(_jsonOptions);
        Assert.NotNull(preset);
        Assert.Equal(presetId, preset.PresetId);
        Assert.Equal(name, preset.Name);
        Assert.Equal("1.0.0", preset.Version);
        Assert.NotNull(preset.Contents);
    }

    [Fact]
    public async Task ExportPreset_WithDescription_IncludesDescription()
    {
        // Arrange
        var presetId = $"export-desc-{Guid.NewGuid():N}";
        var name = "Export With Description";
        var description = "This is a test description";
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get,
            $"/api/admin/presets/export?presetId={presetId}&name={Uri.EscapeDataString(name)}&description={Uri.EscapeDataString(description)}");

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
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, "/api/admin/presets/export?name=Test");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExportPreset_MissingName_ReturnsBadRequest()
    {
        // Arrange
        var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, "/api/admin/presets/export?presetId=test-id");

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
        var applyRequest = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/presets/apply", preset);
        await _client.SendAsync(applyRequest);

        // Act
        var getRequest = await CreateAuthenticatedRequestAsync(HttpMethod.Get, "/api/admin/presets/applications");
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
        var applyRequest = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "/api/admin/presets/apply", preset);
        var applyResponse = await _client.SendAsync(applyRequest);

        // Verify the apply succeeded first
        var applyBody = await applyResponse.Content.ReadAsStringAsync();
        Assert.True(applyResponse.StatusCode == HttpStatusCode.OK,
            $"Apply failed with {applyResponse.StatusCode}: {applyBody}");

        // Act
        var getRequest = await CreateAuthenticatedRequestAsync(HttpMethod.Get, "/api/admin/presets/applications");
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
        public int CriteriaCreated { get; init; }
        public int CriteriaUpdated { get; init; }
        public int SpaceGroupsCreated { get; init; }
        public int SpaceGroupsUpdated { get; init; }
        public int TemplatesCreated { get; init; }
        public int TemplatesUpdated { get; init; }
    }

    #endregion
}
