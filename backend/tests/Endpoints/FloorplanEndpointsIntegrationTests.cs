using System.Net;
using System.Net.Http.Headers;
using Api.Repositories;
using Api.Services;
using Api.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for Floorplan endpoints that go through the full middleware pipeline.
/// Verifies that a Bearer token is required for protected endpoints.
/// </summary>
[Collection("Database collection")]
public class FloorplanEndpointsIntegrationTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _databaseFixture;
    private const string TenantSlug = TestConstants.TenantSlug;

    public FloorplanEndpointsIntegrationTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
        _client = databaseFixture.Factory.CreateClient();
        _client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
    }

    #region GET /sites/{siteId}/floorplan - Get Floorplan Image

    [Fact]
    public async Task GetFloorplanImage_WithBearer_ShouldReturnNotFound()
    {
        // 404 means Bearer token was accepted; site simply doesn't exist.
        var siteId = Guid.NewGuid();
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestConstants.TestBearerToken}");

        var response = await client.GetAsync($"/api/sites/{siteId}/floorplan");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFloorplanImage_WithoutBearer_ShouldReturnUnauthorized()
    {
        var siteId = Guid.NewGuid();
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);

        var response = await client.GetAsync($"/api/sites/{siteId}/floorplan");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region GET /sites/{siteId}/floorplan/metadata - Get Floorplan Metadata

    [Fact]
    public async Task GetFloorplanMetadata_WithBearer_ShouldReturnNotFound()
    {
        var siteId = Guid.NewGuid();
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestConstants.TestBearerToken}");

        var response = await client.GetAsync($"/api/sites/{siteId}/floorplan/metadata");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFloorplanMetadata_WithoutBearer_ShouldReturnUnauthorized()
    {
        var siteId = Guid.NewGuid();
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);

        var response = await client.GetAsync($"/api/sites/{siteId}/floorplan/metadata");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region POST /sites/{siteId}/floorplan - Upload Floorplan

    [Fact]
    public async Task UploadFloorplan_WithoutBearer_ShouldReturnUnauthorized()
    {
        var siteId = Guid.NewGuid();
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG header
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "test.png");

        var response = await client.PostAsync($"/api/sites/{siteId}/floorplan", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadFloorplan_WithBearer_ShouldReturnNotFound()
    {
        // NotFound = authenticated OK; endpoint logic ran and rejected the unknown site.
        var siteId = Guid.NewGuid();
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestConstants.TestBearerToken}");

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG header stub
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "test.png");

        var response = await client.PostAsync($"/api/sites/{siteId}/floorplan", content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region DELETE /sites/{siteId}/floorplan - Delete Floorplan

    [Fact]
    public async Task DeleteFloorplan_WithoutBearer_ShouldReturnUnauthorized()
    {
        var siteId = Guid.NewGuid();
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);

        var response = await client.DeleteAsync($"/api/sites/{siteId}/floorplan");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFloorplan_WithBearer_ShouldReturnNotFound()
    {
        var siteId = Guid.NewGuid();
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestConstants.TestBearerToken}");

        var response = await client.DeleteAsync($"/api/sites/{siteId}/floorplan");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Audit logging

    // A valid PNG whose header the upload validation reads (format + dimensions).
    private static byte[] TestPng() => TestImageFactory.Png(10, 10);

    private (IOrgDbConnectionFactory conn, OrgContext org) DbScope()
    {
        var scope = _databaseFixture.Factory.Services.CreateScope();
        return (
            scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>(),
            scope.ServiceProvider.GetRequiredService<OrgContext>());
    }

    private async Task<Guid> SeedSiteAsync()
    {
        var (connFactory, org) = DbScope();
        await using var conn = connFactory.CreateOrgConnection(org);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO sites (name, code) VALUES (@n, @c) RETURNING id", conn);
        cmd.Parameters.AddWithValue("n", $"Site {Guid.NewGuid():N}"[..20]);
        cmd.Parameters.AddWithValue("c", $"S{Guid.NewGuid():N}"[..12]);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> AuditCountAsync(string action, Guid siteId)
    {
        var (connFactory, org) = DbScope();
        await using var conn = connFactory.CreateOrgConnection(org);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM audit_events WHERE action = @a AND target_type = 'site' AND target_id = @t", conn);
        cmd.Parameters.AddWithValue("a", action);
        cmd.Parameters.AddWithValue("t", siteId.ToString());
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private HttpClient AuthedClient()
    {
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestConstants.TestBearerToken}");
        return client;
    }

    [Fact]
    public async Task UploadAndDownloadAndDelete_AreAuditLogged()
    {
        var siteId = await SeedSiteAsync();
        var client = AuthedClient();
        var png = TestPng();

        // Upload
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(png);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "plan.png");
        var upload = await client.PostAsync($"/api/sites/{siteId}/floorplan", content);
        Assert.True(upload.StatusCode == HttpStatusCode.OK,
            $"upload failed: {upload.StatusCode} — {await upload.Content.ReadAsStringAsync()}");
        Assert.True(await AuditCountAsync("floorplan.upload", siteId) >= 1);

        // Download
        var download = await client.GetAsync($"/api/sites/{siteId}/floorplan");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        // Returned bytes are the decrypted original PNG (encrypted at rest, transparent on read).
        Assert.Equal(png, await download.Content.ReadAsByteArrayAsync());
        Assert.True(await AuditCountAsync("floorplan.download", siteId) >= 1);

        // Delete
        var delete = await client.DeleteAsync($"/api/sites/{siteId}/floorplan");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.True(await AuditCountAsync("floorplan.delete", siteId) >= 1);
    }

    #endregion
}
