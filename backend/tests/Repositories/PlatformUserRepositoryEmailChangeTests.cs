using Api.Repositories;
using Api.Security;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// The pending email change against a real database: only the token's digest is stored, and
/// confirmation looks the row up by the digest of the mailed token.
/// </summary>
[Collection("Database collection")]
public class PlatformUserRepositoryEmailChangeTests
{
    private readonly IPlatformUserRepository _repo;
    private readonly string _cp;

    public PlatformUserRepositoryEmailChangeTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _repo = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
        _cp = fixture.ControlPlaneConnectionString;
    }

    [Fact]
    public async Task SetPendingEmailChange_StoresTheTokenDigest_NotTheToken()
    {
        var userId = await InsertUserAsync();
        try
        {
            var token = SecureTokens.Generate();

            (await _repo.SetPendingEmailChangeAsync(userId, $"{userId:N}@new.example.com", token)).Should().BeTrue();

            var (hash, plaintext) = await ReadTokenColumnsAsync(userId);
            hash.Should().Be(SecureTokens.Sha256Hex(token));
            hash.Should().NotContain(token);
            plaintext.Should().BeNull("the plaintext column is no longer written");
        }
        finally
        {
            await DeleteUserAsync(userId);
        }
    }

    [Fact]
    public async Task ClearPendingEmailChange_RemovesTheTokenDigest()
    {
        var userId = await InsertUserAsync();
        try
        {
            await _repo.SetPendingEmailChangeAsync(userId, $"{userId:N}@new.example.com", SecureTokens.Generate());

            await _repo.ClearPendingEmailChangeAsync(userId);

            (await ReadTokenColumnsAsync(userId)).Hash.Should().BeNull();
        }
        finally
        {
            await DeleteUserAsync(userId);
        }
    }

    [Fact]
    public async Task ConfirmEmailChange_MatchesTheMailedToken_NotItsStoredDigest()
    {
        var userId = await InsertUserAsync();
        try
        {
            var token = SecureTokens.Generate();
            var newEmail = $"{userId:N}@new.example.com";
            await _repo.SetPendingEmailChangeAsync(userId, newEmail, token);

            var byDigest = await _repo.ConfirmEmailChangeAsync(SecureTokens.Sha256Hex(token), NoKeycloak);
            byDigest.Status.Should().Be(EmailChangeConfirmStatus.NotFoundOrExpired);

            var byToken = await _repo.ConfirmEmailChangeAsync(token, NoKeycloak);
            byToken.Status.Should().Be(EmailChangeConfirmStatus.Confirmed);
            byToken.PendingEmail.Should().Be(newEmail);
            (await ReadTokenColumnsAsync(userId)).Hash.Should().BeNull("a used token cannot confirm again");
        }
        finally
        {
            await DeleteUserAsync(userId);
        }
    }

    private static Task NoKeycloak(string? keycloakId, string currentEmail, string pendingEmail, CancellationToken ct) =>
        Task.CompletedTask;

    private async Task<Guid> InsertUserAsync()
    {
        var userId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_cp);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO users (id, email, display_name, status, created_at, updated_at)
            VALUES (@id, @email, 'Email Change User', 'active', NOW(), NOW())", conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", $"{userId:N}@old.example.com");
        await cmd.ExecuteNonQueryAsync();
        return userId;
    }

    private async Task<(string? Hash, string? Plaintext)> ReadTokenColumnsAsync(Guid userId)
    {
        await using var conn = new NpgsqlConnection(_cp);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT email_change_token_hash, email_change_token FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("the user row must exist");
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task DeleteUserAsync(Guid userId)
    {
        await using var conn = new NpgsqlConnection(_cp);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync();
    }
}
