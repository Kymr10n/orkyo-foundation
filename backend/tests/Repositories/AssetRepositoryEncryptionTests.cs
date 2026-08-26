using System.Security.Cryptography;
using System.Text;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// Verifies floorplan/asset blobs are encrypted at rest (the bytes in
/// <c>assets.data</c> are an Orkyo envelope, never plaintext) and decrypt
/// byte-identically on read.
/// </summary>
[Collection("Database collection")]
public class AssetRepositoryEncryptionTests
{
    private readonly IAssetRepository _repo;
    private readonly IOrgDbConnectionFactory _connFactory;
    private readonly OrgContext _orgContext;

    public AssetRepositoryEncryptionTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _repo = scope.ServiceProvider.GetRequiredService<IAssetRepository>();
        _connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        _orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();
    }

    private static byte[] FakePng(string marker) =>
        Encoding.UTF8.GetBytes("\x89PNG\r\n\x1a\n" + marker + new string('x', 64));

    private static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private async Task<(byte[] data, string? encAlgorithm, long sizeBytes)> ReadRawAsync(Guid assetId)
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT data, enc_algorithm, size_bytes FROM assets WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", assetId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (
            (byte[])reader.GetValue(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetInt64(2));
    }

    private async Task<Api.Models.AssetInfo> UpsertAsync(byte[] plaintext)
    {
        var ownerId = Guid.NewGuid();
        return await _repo.UpsertPostgresAssetAsync(
            _orgContext.OrgId, AssetOwnerTypes.Site, ownerId, AssetTypes.Floorplan,
            "plan.png", "image/png", plaintext, Sha256Hex(plaintext), 100, 80, null);
    }

    [Fact]
    public async Task Upsert_StoresEncryptedEnvelope_NotPlaintext()
    {
        var plaintext = FakePng("SECRET_FLOORPLAN_MARKER");
        var asset = await UpsertAsync(plaintext);

        var (raw, encAlgorithm, sizeBytes) = await ReadRawAsync(asset.Id);

        // Stored bytes are the Orkyo binary envelope ("ORK1"), not the plaintext PNG.
        Encoding.ASCII.GetString(raw, 0, 4).Should().Be("ORK1");
        Encoding.UTF8.GetString(raw).Should().NotContain("SECRET_FLOORPLAN_MARKER");
        encAlgorithm.Should().Be("aesgcm256");
        // size_bytes reflects the PLAINTEXT length (user-facing + quota), not ciphertext.
        sizeBytes.Should().Be(plaintext.Length);
    }

    [Fact]
    public async Task GetDownload_DecryptsToOriginalBytes()
    {
        var plaintext = FakePng("ROUNDTRIP");
        var asset = await UpsertAsync(plaintext);

        var download = await _repo.GetDownloadForOwnerAsync(
            _orgContext.OrgId, AssetOwnerTypes.Site, asset.OwnerId, AssetTypes.Floorplan);

        download.Should().NotBeNull();
        download!.Data.Should().Equal(plaintext);
    }
}
