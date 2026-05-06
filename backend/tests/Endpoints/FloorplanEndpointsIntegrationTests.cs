using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
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
        _client.DefaultRequestHeaders.Add("X-Tenant-Slug", TenantSlug);
    }

    #region GET /sites/{siteId}/floorplan - Get Floorplan Image

    [Fact]
    public async Task GetFloorplanImage_WithBearer_ShouldReturnNotFound()
    {
        // 404 means Bearer token was accepted; site simply doesn't exist.
        var siteId = Guid.NewGuid();
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", TenantSlug);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestConstants.TestBearerToken}");

        var response = await client.GetAsync($"/api/sites/{siteId}/floorplan");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFloorplanImage_WithoutBearer_ShouldReturnUnauthorized()
    {
        var siteId = Guid.NewGuid();
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", TenantSlug);

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
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", TenantSlug);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestConstants.TestBearerToken}");

        var response = await client.GetAsync($"/api/sites/{siteId}/floorplan/metadata");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFloorplanMetadata_WithoutBearer_ShouldReturnUnauthorized()
    {
        var siteId = Guid.NewGuid();
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", TenantSlug);

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
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", TenantSlug);

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG header
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "test.png");

        var response = await client.PostAsync($"/api/sites/{siteId}/floorplan", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadFloorplan_WithBearer_ShouldReturnBadRequest()
    {
        // BadRequest = authenticated OK, rejected for invalid image format (4-byte stub).
        var siteId = Guid.NewGuid();
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", TenantSlug);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestConstants.TestBearerToken}");

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG header stub
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "test.png");

        var response = await client.PostAsync($"/api/sites/{siteId}/floorplan", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region DELETE /sites/{siteId}/floorplan - Delete Floorplan

    [Fact]
    public async Task DeleteFloorplan_WithoutBearer_ShouldReturnUnauthorized()
    {
        var siteId = Guid.NewGuid();
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", TenantSlug);

        var response = await client.DeleteAsync($"/api/sites/{siteId}/floorplan");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFloorplan_WithBearer_ShouldReturnNotFound()
    {
        var siteId = Guid.NewGuid();
        var client = _databaseFixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", TenantSlug);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestConstants.TestBearerToken}");

        var response = await client.DeleteAsync($"/api/sites/{siteId}/floorplan");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion
}
