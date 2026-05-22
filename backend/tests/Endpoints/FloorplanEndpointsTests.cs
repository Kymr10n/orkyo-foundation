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
/// Integration tests for DB-backed floorplan endpoints.
/// </summary>
[Collection("Database collection")]
public class FloorplanEndpointsTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly HttpClient _client;
    private readonly string _connectionString;

    public FloorplanEndpointsTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
        _connectionString = $"Host=localhost;Port={fixture.DatabasePort};Database={TestConstants.TenantDatabase};Username=postgres;Password=postgres";
    }

    [Fact]
    public async Task UploadFloorplan_WithValidPng_ShouldPersistAssetAndReturnMetadata()
    {
        var siteId = await CreateTestSiteAsync();

        var response = await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestPngImage(), "image/png", "floorplan.png"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        var metadata = body.GetProperty("metadata");
        metadata.GetProperty("mimeType").GetString().Should().Be("image/png");
        metadata.GetProperty("fileName").GetString().Should().Be("floorplan.png");

        var asset = await ReadFloorplanAssetAsync(siteId);
        asset.Should().NotBeNull();
        asset!.ContentType.Should().Be("image/png");
        asset.Width.Should().Be(10);
        asset.Height.Should().Be(10);
        asset.DataLength.Should().BeGreaterThan(0);
        asset.Checksum.Should().HaveLength(64);
    }

    [Fact]
    public async Task UploadFloorplan_WithValidJpeg_ShouldPersistAsset()
    {
        var siteId = await CreateTestSiteAsync();

        var response = await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestJpegImage(), "image/jpeg", "floorplan.jpg"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var asset = await ReadFloorplanAssetAsync(siteId);
        asset!.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task UploadFloorplan_ReplacingExisting_ShouldKeepOneCurrentAsset()
    {
        var siteId = await CreateTestSiteAsync();

        (await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestPngImage(), "image/png", "one.png")))
            .EnsureSuccessStatusCode();
        var first = await ReadFloorplanAssetAsync(siteId);

        (await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestJpegImage(), "image/jpeg", "two.jpg")))
            .EnsureSuccessStatusCode();
        var second = await ReadFloorplanAssetAsync(siteId);

        second!.Id.Should().Be(first!.Id);
        second.FileName.Should().Be("two.jpg");
        second.ContentType.Should().Be("image/jpeg");
        (await CountFloorplanAssetsAsync(siteId)).Should().Be(1);
    }

    [Fact]
    public async Task UploadFloorplan_WithWrongFieldName_ShouldReturn400()
    {
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

    [Fact]
    public async Task GetFloorplan_WithExistingImage_ShouldReturnBytesAndHeaders()
    {
        var siteId = await CreateTestSiteAsync();
        (await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestPngImage(), "image/png", "floorplan.png")))
            .EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/api/sites/{siteId}/floorplan");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        response.Content.Headers.ContentLength.Should().BeGreaterThan(0);
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.Tag.Should().MatchRegex("^\"[0-9a-f]{64}\"$");
        response.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromMinutes(5));
        response.Headers.Vary.Should().Contain("Cookie");
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

    [Fact]
    public async Task GetFloorplanMetadata_WithExistingImage_ShouldReturnDbBackedDetails()
    {
        var siteId = await CreateTestSiteAsync();
        (await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestPngImage(), "image/png", "floorplan.png")))
            .EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/api/sites/{siteId}/floorplan/metadata");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("assetId").GetGuid().Should().NotBeEmpty();
        body.GetProperty("fileName").GetString().Should().Be("floorplan.png");
        body.GetProperty("mimeType").GetString().Should().Be("image/png");
        body.GetProperty("checksumSha256").GetString().Should().HaveLength(64);
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

    [Fact]
    public async Task DeleteFloorplan_WithExistingImage_ShouldRemoveAsset()
    {
        var siteId = await CreateTestSiteAsync();
        (await _client.PostAsync(
            $"/api/sites/{siteId}/floorplan",
            BuildImageMultipartContent(CreateTestPngImage(), "image/png", "floorplan.png")))
            .EnsureSuccessStatusCode();

        var response = await _client.DeleteAsync($"/api/sites/{siteId}/floorplan");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadFloorplanAssetAsync(siteId)).Should().BeNull();
    }

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

    private async Task<AssetRow?> ReadFloorplanAssetAsync(Guid siteId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, file_name, content_type, octet_length(data), checksum_sha256, width_px, height_px
            FROM assets
            WHERE tenant_id = @tenantId
              AND owner_type = 'site'
              AND owner_id = @siteId
              AND asset_type = 'floorplan'", conn);
        cmd.Parameters.AddWithValue("tenantId", TenantId);
        cmd.Parameters.AddWithValue("siteId", siteId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new AssetRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetInt32(5),
            reader.GetInt32(6));
    }

    private async Task<int> CountFloorplanAssetsAsync(Guid siteId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT COUNT(*)
            FROM assets
            WHERE tenant_id = @tenantId
              AND owner_type = 'site'
              AND owner_id = @siteId
              AND asset_type = 'floorplan'", conn);
        cmd.Parameters.AddWithValue("tenantId", TenantId);
        cmd.Parameters.AddWithValue("siteId", siteId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
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

    private static byte[] CreateTestJpegImage()
    {
        using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(10, 10);
        image.Mutate(x => x.BackgroundColor(Color.Blue));
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private sealed record AssetRow(
        Guid Id,
        string FileName,
        string ContentType,
        int DataLength,
        string Checksum,
        int Width,
        int Height);
}
