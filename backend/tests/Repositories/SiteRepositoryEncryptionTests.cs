using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// Verifies sites.description / sites.address are encrypted at rest (stored as
/// Orkyo envelopes) yet returned as plaintext through the repository.
/// </summary>
[Collection("Database collection")]
public class SiteRepositoryEncryptionTests
{
    private readonly ISiteRepository _repo;
    private readonly IOrgDbConnectionFactory _connFactory;
    private readonly OrgContext _orgContext;

    public SiteRepositoryEncryptionTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _repo = scope.ServiceProvider.GetRequiredService<ISiteRepository>();
        _connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        _orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();
    }

    private async Task<(string? description, string? address)> ReadRawAsync(Guid siteId)
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT description, address FROM sites WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", siteId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    [Fact]
    public async Task Create_EncryptsDescriptionAndAddress_ButReturnsPlaintext()
    {
        const string desc = "CONFIDENTIAL facility notes";
        const string addr = "123 Secret Street, Hidden City";
        var code = $"S{Guid.NewGuid():N}"[..12];

        var created = await _repo.CreateAsync(code, "Plant A", desc, addr);

        // Returned object is plaintext.
        created.Description.Should().Be(desc);
        created.Address.Should().Be(addr);

        // Raw DB columns are envelopes, not plaintext.
        var (rawDesc, rawAddr) = await ReadRawAsync(created.Id);
        rawDesc.Should().StartWith("orkyoenc:");
        rawDesc.Should().NotContain("CONFIDENTIAL");
        rawAddr.Should().StartWith("orkyoenc:");
        rawAddr.Should().NotContain("Secret Street");

        // GetById decrypts.
        var fetched = await _repo.GetByIdAsync(created.Id);
        fetched!.Description.Should().Be(desc);
        fetched.Address.Should().Be(addr);
    }

    [Fact]
    public async Task Create_LeavesNullFieldsNull()
    {
        var code = $"S{Guid.NewGuid():N}"[..12];
        var created = await _repo.CreateAsync(code, "Plant B", null, null);

        var (rawDesc, rawAddr) = await ReadRawAsync(created.Id);
        rawDesc.Should().BeNull();
        rawAddr.Should().BeNull();
        created.Description.Should().BeNull();
        created.Address.Should().BeNull();
    }

    [Fact]
    public async Task Update_ReEncryptsChangedValues()
    {
        var code = $"S{Guid.NewGuid():N}"[..12];
        var created = await _repo.CreateAsync(code, "Plant C", "old", "old addr");

        await _repo.UpdateAsync(created.Id, code, "Plant C", "new secret", "new addr");

        var (rawDesc, _) = await ReadRawAsync(created.Id);
        rawDesc.Should().StartWith("orkyoenc:");
        rawDesc.Should().NotContain("new secret");

        var fetched = await _repo.GetByIdAsync(created.Id);
        fetched!.Description.Should().Be("new secret");
        fetched.Address.Should().Be("new addr");
    }
}
