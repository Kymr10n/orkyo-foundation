using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for Floorplan endpoints — drive real handlers via HttpClient
/// so endpoint code is exercised end-to-end (middleware → handler → DB).
/// Auth-failure and 404 paths live in <see cref="FloorplanEndpointsIntegrationTests"/>.
/// </summary>
[Collection("Database collection")]
public class FloorplanEndpointsTests : IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly string _connectionString;
    private readonly string _storagePath;

    public FloorplanEndpointsTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
        _connectionString = $"Host=localhost;Port={fixture.DatabasePort};Database={TestConstants.TenantDatabase};Username=postgres;Password=postgres";
        // Must match FILE_STORAGE_PATH in FoundationWebApplicationFactory.
        _storagePath = "/tmp/orkyo-test-storage";
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    // ── POST /api/sites/{siteId}/floorplan ──────────────────────────────

    [Fact]
    public async Task UploadFloorplan_WithValidPng_ShouldPersistMetadataAndReturnOk()
    {
        var siteId = await CreateTestSiteAsync();

        var response = await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestPngImage(), "image/png", "floorplan.png"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("metadata").GetProperty("mimeType").GetString().Should().Be("image/png");

        var meta = await ReadSiteMetadataAsync(siteId);
        meta.MimeType.Should().Be("image/png");
        meta.Width.Should().Be(10);
        meta.Height.Should().Be(10);
        meta.Path.Should().NotBeNull();
        File.Exists(Path.Combine(_storagePath, meta.Path!)).Should().BeTrue();
    }

    [Fact]
    public async Task UploadFloorplan_ReplacingExisting_ShouldDeleteOldFile()
    {
        var siteId = await CreateTestSiteAsync();

        var first = await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestPngImage(), "image/png", "one.png"));
        first.EnsureSuccessStatusCode();
        var firstPath = (await ReadSiteMetadataAsync(siteId)).Path!;
        var firstFull = Path.Combine(_storagePath, firstPath);
        File.Exists(firstFull).Should().BeTrue();

        var second = await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestPngImage(), "image/png", "two.png"));
        second.EnsureSuccessStatusCode();

        var secondPath = (await ReadSiteMetadataAsync(siteId)).Path!;
        secondPath.Should().NotBe(firstPath);
        File.Exists(firstFull).Should().BeFalse();
        File.Exists(Path.Combine(_storagePath, secondPath)).Should().BeTrue();
    }

    [Fact]
    public async Task UploadFloorplan_WithWrongFieldName_ShouldReturn400()
    {
        // Endpoint reads Files.GetFile("file"); a different field name triggers the "No file" branch.
        var siteId = await CreateTestSiteAsync();

        var content = new MultipartFormDataContent();
        var wrong = new ByteArrayContent(CreateTestPngImage());
        wrong.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(wrong, "not-file", "floorplan.png");

        var response = await _client.PostAsync($"/api/sites/{siteId}/floorplan", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain("No file");
    }

    [Fact]
    public async Task UploadFloorplan_WithInvalidImage_ShouldReturn400()
    {
        // 4 bytes of PNG-header-ish but not a real image — SaveFloorplanAsync throws ArgumentException.
        var siteId = await CreateTestSiteAsync();

        var response = await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png", "bad.png"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadFloorplan_ForNonExistentSite_ShouldReturn404()
    {
        var siteId = Guid.NewGuid();

        var response = await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestPngImage(), "image/png", "floorplan.png"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET /api/sites/{siteId}/floorplan ───────────────────────────────

    [Fact]
    public async Task GetFloorplan_WithExistingImage_ShouldReturnBytesAndETag()
    {
        var siteId = await CreateTestSiteAsync();
        (await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestPngImage(), "image/png", "floorplan.png")))
            .EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/api/sites/{siteId}/floorplan");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromMinutes(5));
        (await response.Content.ReadAsByteArrayAsync()).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetFloorplan_WithMatchingIfNoneMatch_ShouldReturn304()
    {
        var siteId = await CreateTestSiteAsync();
        (await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestPngImage(), "image/png", "floorplan.png")))
            .EnsureSuccessStatusCode();

        var first = await _client.GetAsync($"/api/sites/{siteId}/floorplan");
        var etag = first.Headers.ETag!.Tag;

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sites/{siteId}/floorplan");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var cached = await _client.SendAsync(request);

        cached.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task GetFloorplan_WhenSiteHasNoImage_ShouldReturn404()
    {
        var siteId = await CreateTestSiteAsync();

        var response = await _client.GetAsync($"/api/sites/{siteId}/floorplan");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET /api/sites/{siteId}/floorplan/metadata ──────────────────────

    [Fact]
    public async Task GetFloorplanMetadata_WithExistingImage_ShouldReturnDetails()
    {
        var siteId = await CreateTestSiteAsync();
        (await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestPngImage(), "image/png", "floorplan.png")))
            .EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/api/sites/{siteId}/floorplan/metadata");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("mimeType").GetString().Should().Be("image/png");
        body.GetProperty("widthPx").GetInt32().Should().Be(10);
        body.GetProperty("heightPx").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task GetFloorplanMetadata_WhenSiteHasNoImage_ShouldReturnNullJson()
    {
        var siteId = await CreateTestSiteAsync();

        var response = await _client.GetAsync($"/api/sites/{siteId}/floorplan/metadata");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Trim().Should().Be("null");
    }

    // ── DELETE /api/sites/{siteId}/floorplan ───────────────────────────

    [Fact]
    public async Task DeleteFloorplan_WithExistingImage_ShouldRemoveFileAndClearMetadata()
    {
        var siteId = await CreateTestSiteAsync();
        (await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestPngImage(), "image/png", "floorplan.png")))
            .EnsureSuccessStatusCode();
        var path = (await ReadSiteMetadataAsync(siteId)).Path!;
        var full = Path.Combine(_storagePath, path);
        File.Exists(full).Should().BeTrue();

        var response = await _client.DeleteAsync($"/api/sites/{siteId}/floorplan");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        File.Exists(full).Should().BeFalse();
        (await ReadSiteMetadataAsync(siteId)).Path.Should().BeNull();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private async Task<Guid> CreateTestSiteAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        var code = $"S-{id:N}".Substring(0, 10);
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO sites (id, name, code) VALUES (@id, @name, @code)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", $"Site {id:N}");
        cmd.Parameters.AddWithValue("code", code);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<SiteFloorplanRow> ReadSiteMetadataAsync(Guid siteId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"SELECT floorplan_image_path, floorplan_mime_type, floorplan_width_px, floorplan_height_px
              FROM sites WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", siteId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"Site {siteId} not found");
        return new SiteFloorplanRow(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
            reader.IsDBNull(3) ? 0 : reader.GetInt32(3));
    }

    private static MultipartFormDataContent BuildImageMultipartContent(byte[] bytes, string mimeType, string filename)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        content.Add(file, "file", filename);
        return content;
    }

    private static byte[] CreateTestPngImage()
    {
        using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(10, 10);
        image.Mutate(x => x.BackgroundColor(Color.Green));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private sealed record SiteFloorplanRow(string? Path, string? MimeType, int Width, int Height);
}
