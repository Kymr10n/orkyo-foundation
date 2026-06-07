using Api.Models;
using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// Verifies person_profiles.notes is encrypted at rest yet returned as plaintext
/// through the repository. email is intentionally left plaintext (lookup field).
/// </summary>
[Collection("Database collection")]
public class PersonProfileEncryptionTests
{
    private readonly IPersonProfileRepository _repo;
    private readonly IOrgDbConnectionFactory _connFactory;
    private readonly OrgContext _orgContext;

    public PersonProfileEncryptionTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _repo = scope.ServiceProvider.GetRequiredService<IPersonProfileRepository>();
        _connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        _orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();
    }

    private async Task<Guid> SeedPersonResourceAsync()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO resources (resource_type_id, name, allocation_mode)
              SELECT id, @name, 'Exclusive' FROM resource_types WHERE key = 'person'
              RETURNING id", conn);
        cmd.Parameters.AddWithValue("name", $"Test Person {Guid.NewGuid():N}"[..30]);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<(string? notes, string? email)> ReadRawAsync(Guid resourceId)
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT notes, email::text FROM person_profiles WHERE resource_id = @id", conn);
        cmd.Parameters.AddWithValue("id", resourceId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    [Fact]
    public async Task Upsert_EncryptsNotes_ButLeavesEmailPlaintext()
    {
        var resourceId = await SeedPersonResourceAsync();

        var saved = await _repo.UpsertAsync(resourceId, new UpsertPersonProfileRequest
        {
            Email = "person@example.com",
            Notes = "CONFIDENTIAL employee note",
        });

        saved.Notes.Should().Be("CONFIDENTIAL employee note");

        var (rawNotes, rawEmail) = await ReadRawAsync(resourceId);
        rawNotes.Should().StartWith("orkyoenc:");
        rawNotes.Should().NotContain("CONFIDENTIAL");
        rawEmail.Should().Be("person@example.com"); // email is a lookup field — plaintext

        var fetched = await _repo.GetByResourceIdAsync(resourceId);
        fetched!.Notes.Should().Be("CONFIDENTIAL employee note");
    }
}
