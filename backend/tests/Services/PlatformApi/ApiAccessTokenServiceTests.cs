using Api.Security;
using Api.Services;
using Api.Services.PlatformApi;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orkyo.Shared;
using Xunit;

namespace Orkyo.Foundation.Tests.Services.PlatformApi;

/// <summary>
/// The write-capable credential class. Two things are guarded here that the reporting token never
/// needed: a real scope system (reporting hardcodes one value), and a pepper of its own.
/// </summary>
public class ApiAccessTokenServiceTests
{
    private static ApiAccessTokenService Create(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        return new ApiAccessTokenService(
            Mock.Of<IDbConnectionFactory>(),
            configuration,
            NullLogger<ApiAccessTokenService>.Instance);
    }

    private static ApiAccessTokenService CreateValid() =>
        Create((ConfigKeys.ApiAccessTokenPepper, "a-dedicated-pepper"));

    // ── Pepper resolution ────────────────────────────────────────────────────

    [Fact]
    public void WithNeitherKeySet_RefusesToStart()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => Create());

        thrown.Message.Should().Contain(ConfigKeys.ApiAccessTokenPepper);
        thrown.Message.Should().Contain(ConfigKeys.KeycloakBackendClientSecret);
    }

    [Fact]
    public void WithBothKeysPresentButEmpty_RefusesToStart()
    {
        Assert.Throws<InvalidOperationException>(() => Create(
            (ConfigKeys.ApiAccessTokenPepper, ""),
            (ConfigKeys.KeycloakBackendClientSecret, "")));
    }

    [Fact]
    public void WithOnlyTheKeycloakSecretSet_FallsBackToItAndStarts()
    {
        Create((ConfigKeys.KeycloakBackendClientSecret, "a-real-client-secret"))
            .Should().NotBeNull();
    }

    [Fact]
    public void UsesItsOwnPepperKey_NotTheReportingOne()
    {
        // Sharing the reporting pepper would mean one leaked config value compromises both the
        // read-only and the write-capable credential class.
        Assert.Throws<InvalidOperationException>(() =>
            Create((ConfigKeys.ReportingTokenPepper, "reporting-only")));
    }

    // ── Scope validation (no DB access: these all fail before the insert) ─────

    [Fact]
    public async Task CreateAsync_RejectsAnUnknownScope()
    {
        var service = CreateValid();

        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            Guid.NewGuid(), "agent", ["schedule:read", "tenant:admin"], null, null));

        thrown.Message.Should().Contain("tenant:admin");
    }

    [Fact]
    public async Task CreateAsync_RejectsAnEmptyScopeList()
    {
        // A token with no scopes would map to TenantRole.None and be silently useless; refusing at
        // creation makes the mistake visible where it is made.
        var service = CreateValid();

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            Guid.NewGuid(), "agent", [], null, null));
    }

    [Fact]
    public async Task CreateAsync_RejectsAScopeDifferingOnlyByCase()
    {
        // Scope comparison is ordinal everywhere, so "Schedule:Write" must not be accepted and
        // then silently fail to grant write at request time.
        var service = CreateValid();

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            Guid.NewGuid(), "agent", ["Schedule:Write"], null, null));
    }

    // ── Record semantics ─────────────────────────────────────────────────────

    [Fact]
    public void ARevokedTokenIsNotActive()
    {
        new ApiAccessTokenRecord { RevokedAtUtc = DateTime.UtcNow.AddMinutes(-1) }
            .IsActive.Should().BeFalse();
    }

    [Fact]
    public void AnExpiredTokenIsNotActive()
    {
        new ApiAccessTokenRecord { ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1) }
            .IsActive.Should().BeFalse();
    }

    [Fact]
    public void ATokenWithNoExpiryIsActive()
    {
        new ApiAccessTokenRecord().IsActive.Should().BeTrue();
    }

    [Fact]
    public void EffectiveRole_ComesFromTheStoredScopes()
    {
        new ApiAccessTokenRecord { Scopes = PlatformApiScopes.ScheduleWrite }
            .EffectiveRole.Should().Be(TenantRole.Editor);
        new ApiAccessTokenRecord { Scopes = PlatformApiScopes.ScheduleRead }
            .EffectiveRole.Should().Be(TenantRole.Viewer);
    }

    [Fact]
    public async Task ValidateAsync_RejectsAReportingTokenWithoutTouchingTheDatabase()
    {
        // The mock connection factory throws if used, so reaching the DB here would fail the test.
        // That is the point: a token of another class is rejected on its prefix alone.
        var service = CreateValid();

        var result = await service.ValidateAsync("orkyo_rpt_abcdefgh_c29tZXNlY3JldA");

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bearer orkyo_api_x")]
    [InlineData("orkyo_api_malformed")]
    public async Task ValidateAsync_RejectsMalformedTokensWithoutTouchingTheDatabase(string raw)
    {
        (await CreateValid().ValidateAsync(raw)).Should().BeNull();
    }
}
