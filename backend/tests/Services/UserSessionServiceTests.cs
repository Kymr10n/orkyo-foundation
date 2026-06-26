using Api.Security;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Services;

[Collection("Database collection")]
public class UserSessionServiceTests
{
    private readonly FoundationWebApplicationFactory _factory;

    public UserSessionServiceTests(DatabaseFixture databaseFixture)
    {
        _factory = databaseFixture.Factory;
    }

    private IUserSessionService Service(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IUserSessionService>();

    private static string Sid() => Guid.NewGuid().ToString();

    [Fact]
    public async Task Upsert_InsertsThenUpdates_ParsingUserAgent_NoDuplicate()
    {
        var userId = await DatabaseTestUtils.CreateTestUserAsync($"usersession-upsert-{Guid.NewGuid():N}@test.local");
        var sid = Sid();
        using var scope = _factory.Services.CreateScope();
        var svc = Service(scope);

        await svc.UpsertAsync(userId, sid, "203.0.113.7",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0 Safari/537.36");

        var afterInsert = (await svc.GetByUserAsync(userId)).Single(r => r.KeycloakSessionId == sid);
        afterInsert.IpAddress.Should().Be("203.0.113.7");
        afterInsert.Browser.Should().Be("Chrome");
        afterInsert.OperatingSystem.Should().Be("Windows");
        afterInsert.DeviceType.Should().Be(UserAgentParser.DeviceDesktop);

        // Re-login on the same Keycloak session updates in place (unique sid).
        await svc.UpsertAsync(userId, sid, "198.51.100.42",
            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Version/17.0 Mobile Safari/604.1");

        var rows = (await svc.GetByUserAsync(userId)).Where(r => r.KeycloakSessionId == sid).ToList();
        rows.Should().HaveCount(1);
        rows[0].IpAddress.Should().Be("198.51.100.42");
        rows[0].Browser.Should().Be("Safari");
        rows[0].DeviceType.Should().Be(UserAgentParser.DeviceMobile);
    }

    [Fact]
    public async Task PruneExcept_RemovesRowsNotInLiveSet()
    {
        var userId = await DatabaseTestUtils.CreateTestUserAsync($"usersession-prune-{Guid.NewGuid():N}@test.local");
        var live = Sid();
        var stale = Sid();
        using var scope = _factory.Services.CreateScope();
        var svc = Service(scope);

        await svc.UpsertAsync(userId, live, "10.0.0.1", "Chrome");
        await svc.UpsertAsync(userId, stale, "10.0.0.2", "Chrome");

        await svc.PruneExceptAsync(userId, new[] { live });

        var remaining = await svc.GetByUserAsync(userId);
        remaining.Select(r => r.KeycloakSessionId).Should().BeEquivalentTo(new[] { live });
    }

    [Fact]
    public async Task PruneExcept_EmptyLiveSet_RemovesAll()
    {
        var userId = await DatabaseTestUtils.CreateTestUserAsync($"usersession-pruneall-{Guid.NewGuid():N}@test.local");
        using var scope = _factory.Services.CreateScope();
        var svc = Service(scope);

        await svc.UpsertAsync(userId, Sid(), "10.0.0.1", "Chrome");
        await svc.UpsertAsync(userId, Sid(), "10.0.0.2", "Chrome");

        await svc.PruneExceptAsync(userId, Array.Empty<string>());

        (await svc.GetByUserAsync(userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_And_RemoveAll_DeleteRows()
    {
        var userId = await DatabaseTestUtils.CreateTestUserAsync($"usersession-remove-{Guid.NewGuid():N}@test.local");
        var a = Sid();
        var b = Sid();
        using var scope = _factory.Services.CreateScope();
        var svc = Service(scope);

        await svc.UpsertAsync(userId, a, "10.0.0.1", "Chrome");
        await svc.UpsertAsync(userId, b, "10.0.0.2", "Chrome");

        await svc.RemoveAsync(a);
        (await svc.GetByUserAsync(userId)).Select(r => r.KeycloakSessionId).Should().BeEquivalentTo(new[] { b });

        await svc.RemoveAllForUserAsync(userId);
        (await svc.GetByUserAsync(userId)).Should().BeEmpty();
    }
}
