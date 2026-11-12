using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Api.Services.Transfer;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// The transfer routes through the whole pipeline: who may call them, what the archive
/// upload accepts, and that a tenant's own archive loads back into it.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Database)]
[Collection("Database collection")]
public class TransferEndpointsIntegrationTests(DatabaseFixture fixture)
{
    private const string Export = "/api/admin/transfer/export";
    private const string Validate = "/api/admin/transfer/import/validate";
    private const string Import = "/api/admin/transfer/import";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static ByteArrayContent ArchiveContent(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return content;
    }

    private async Task<byte[]> ExportAsync()
    {
        var response = await fixture.CreateAuthorizedClient().GetAsync(Export);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync();
    }

    [Theory]
    [InlineData("viewer")]
    [InlineData("editor")]
    public async Task EveryRoute_IsForTenantAdminsOnly(string role)
    {
        var client = fixture.CreateClientWithRole(role);

        (await client.GetAsync(Export)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync(Validate, ArchiveContent([]))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync($"{Import}?confirmation={TestConstants.TenantSlug}", ArchiveContent([])))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Export_WithoutASession_IsUnauthorized()
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TestConstants.TenantSlug);

        (await client.GetAsync(Export)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Export_DownloadsAnArchiveNamedAfterTheTenant()
    {
        var response = await fixture.CreateAuthorizedClient().GetAsync(Export);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        response.Content.Headers.ContentDisposition!.FileName.Should()
            .StartWith($"orkyo-transfer-{TestConstants.TenantSlug}-").And.EndWith(".orkyoexport");

        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        zip.Entries.Select(e => e.FullName).Should()
            .Contain([TenantArchive.ManifestEntry, TenantArchive.UsersEntry, "tables/sites.ndjson"]);
    }

    [Fact]
    public async Task Validate_AcceptsTheTenantsOwnArchive()
    {
        var response = await fixture.CreateAuthorizedClient().PostAsync(Validate, ArchiveContent(await ExportAsync()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<TransferValidationResult>(Json))!;
        result.Blockers.Should().BeEmpty();
        result.SourceTenantSlug.Should().Be(TestConstants.TenantSlug);
        result.UnmatchedEmails.Should().BeEmpty("every user of the archive is a user of this tenant");
    }

    [Fact]
    public async Task Import_OfTheTenantsOwnArchive_PutsTheSameDataBack()
    {
        var client = fixture.CreateAuthorizedClient();
        var archive = await ExportAsync();

        var response = await client.PostAsync($"{Import}?confirmation={TestConstants.TenantSlug}", ArchiveContent(archive));

        var stream = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, stream);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        stream.Should().StartWith("event: progress\ndata: {\"step\":\"erase\"");
        stream.Should().Contain("{\"step\":\"load\",\"percent\":100}");

        // The result is the last event.
        const string resultEvent = "event: result\ndata: ";
        stream.Should().Contain(resultEvent).And.NotContain("event: error");
        var result = JsonSerializer.Deserialize<TransferImportResult>(
            stream[(stream.LastIndexOf(resultEvent, StringComparison.Ordinal) + resultEvent.Length)..].Trim(), Json)!;
        result.PlaceholderUsers.Should().Be(0);
        result.Rows.Should().BeGreaterThan(0);

        // The site and the space every other test in this collection relies on are still there.
        (await client.GetAsync($"/api/sites/{DatabaseFixture.SiteId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/resources/{DatabaseFixture.SpaceId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Import_WithTheWrongConfirmation_IsABadRequest()
    {
        var response = await fixture.CreateAuthorizedClient()
            .PostAsync($"{Import}?confirmation=someone-else", ArchiveContent(await ExportAsync()));

        // Refused before the import reported anything: a status code, not a stream.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Import_OfSomethingThatIsNotAnArchive_IsUnprocessable()
    {
        var response = await fixture.CreateAuthorizedClient().PostAsync(Validate, ArchiveContent("nope"u8.ToArray()));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ApiErrorCodes.TransferArchiveInvalid);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Import_PastTheCeiling_WithoutADeclaredLength_IsTooLarge()
    {
        // A chunked upload states no length up front, so only the count during the copy stops it.
        var content = new StreamContent(new UnknownLengthStream(FoundationWebApplicationFactory.TransferMaxArchiveBytes + 1));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        var response = await fixture.CreateAuthorizedClient().PostAsync(Validate, content);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    private sealed class UnknownLengthStream(long length) : Stream
    {
        private long _remaining = length;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, _remaining);
            Array.Clear(buffer, offset, read);
            _remaining -= read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Import_PastTheCeiling_IsTooLarge()
    {
        var oversized = new byte[FoundationWebApplicationFactory.TransferMaxArchiveBytes + 1];

        var response = await fixture.CreateAuthorizedClient().PostAsync(Validate, ArchiveContent(oversized));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ApiErrorCodes.TransferArchiveTooLarge);
    }
}
