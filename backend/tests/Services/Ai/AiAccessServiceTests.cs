using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Api.Security.Features;
using Api.Services;
using Api.Services.Ai;

namespace Orkyo.Foundation.Tests.Services.Ai;

/// <summary>
/// The access rules are the whole security story of the assistant surface, so they are
/// tested directly rather than through an endpoint: deny by default, admins exempt,
/// budgets enforced, and a new month starting clean.
/// </summary>
public class AiAccessServiceTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IAiAllowanceRepository> _allowances = new();
    private readonly Mock<IAiCredentialService> _credentials = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<IAuthorizationContext> _authorization = new();
    private readonly Mock<ICurrentPrincipal> _principal = new();
    private readonly Mock<ITenantUserService> _tenantUsers = new();
    private readonly Mock<IAccountMutationGuard> _accountGuard = new();

    public AiAccessServiceTests()
    {
        // The happy path everywhere: entitled workspace, key present, ordinary member.
        _featureGate.Setup(g => g.IsEnabledAsync(FeatureKeys.AiAssistant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _credentials.Setup(c => c.IsConfiguredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _authorization.SetupGet(a => a.IsAdmin).Returns(false);
        _principal.SetupGet(p => p.UserId).Returns(UserId);
        // No daily limits and an ordinary (non-shared) account unless a test says otherwise,
        // which is what every workspace looks like until an admin sets one.
        _allowances.Setup(a => a.GetDailyLimitsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiDailyLimits());
        _accountGuard.Setup(g => g.IsAccountLocked(It.IsAny<ICurrentPrincipal>())).Returns(false);
    }

    private AiAccessService CreateSut() => new(
        _allowances.Object, _credentials.Object, _featureGate.Object, _authorization.Object,
        _principal.Object, _tenantUsers.Object, _accountGuard.Object,
        new OrgContext { OrgId = OrgId, OrgSlug = "acme", DbConnectionString = "Host=localhost" });

    [Fact]
    public async Task Member_WithoutAllowanceRow_IsDenied()
    {
        GrantNothing();

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("not_allowed");
    }

    [Fact]
    public async Task Admin_WithoutAllowanceRow_IsAllowedAndUnlimited()
    {
        _authorization.SetupGet(a => a.IsAdmin).Returns(true);
        GrantNothing();

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeTrue();
        decision.MonthlyTokenLimit.Should().BeNull();
    }

    [Fact]
    public async Task Member_WithNullLimit_IsAllowedAndUnlimited()
    {
        GrantAllowance(monthlyTokenLimit: null);
        SetUsage(inputTokens: 900_000, outputTokens: 900_000);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeTrue();
        decision.MonthlyTokenLimit.Should().BeNull();
    }

    [Fact]
    public async Task Member_WithZeroLimit_IsBlocked()
    {
        GrantAllowance(monthlyTokenLimit: 0);
        SetUsage(inputTokens: 0, outputTokens: 0);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("allowance_exhausted");
    }

    [Fact]
    public async Task Member_UnderLimit_IsAllowed()
    {
        GrantAllowance(monthlyTokenLimit: 10_000);
        SetUsage(inputTokens: 4_000, outputTokens: 1_000);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeTrue();
        decision.UsedTotalTokens.Should().Be(5_000);
    }

    [Fact]
    public async Task Member_AtLimit_IsExhausted()
    {
        GrantAllowance(monthlyTokenLimit: 10_000);
        SetUsage(inputTokens: 6_000, outputTokens: 4_000);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("allowance_exhausted");
        decision.UsedTotalTokens.Should().Be(10_000);
    }

    [Fact]
    public async Task Member_WithNoUsageRowThisMonth_StartsFromZero()
    {
        // A new calendar month lands on a new row, so an exhausted user is allowed again
        // without anything having to reset them.
        GrantAllowance(monthlyTokenLimit: 10_000);
        _allowances.Setup(a => a.GetUsageAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiUsageRow?)null);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeTrue();
        decision.UsedTotalTokens.Should().Be(0);
    }

    [Fact]
    public async Task UnentitledWorkspace_IsDeniedBeforeAnythingElseIsChecked()
    {
        _featureGate.Setup(g => g.IsEnabledAsync(FeatureKeys.AiAssistant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("not_entitled");
        _credentials.Verify(c => c.IsConfiguredAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WorkspaceWithoutKey_IsNotConfigured()
    {
        _credentials.Setup(c => c.IsConfiguredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("not_configured");
    }

    [Fact]
    public async Task SetAllowance_RejectsNegativeLimit()
    {
        var act = () => CreateSut().SetAllowanceAsync(UserId, -1, actorUserId: null);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task RecordUsage_WritesIntoTheCurrentMonth()
    {
        var expectedMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        // The subject is the principal, not an argument — that is the point of the change.
        await CreateSut().RecordUsageAsync(inputTokens: 120, outputTokens: 340);

        _allowances.Verify(a => a.RecordUsageAsync(
            UserId, expectedMonth, 120, 340, It.IsAny<CancellationToken>()), Times.Once);
    }

    private void GrantNothing() =>
        _allowances.Setup(a => a.GetAllowanceAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiAllowanceRow?)null);

    private void GrantAllowance(long? monthlyTokenLimit) =>
        _allowances.Setup(a => a.GetAllowanceAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiAllowanceRow { UserId = UserId, MonthlyTokenLimit = monthlyTokenLimit });

    private void SetUsage(long inputTokens, long outputTokens) =>
        _allowances.Setup(a => a.GetUsageAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiUsageRow
            {
                UserId = UserId,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                Turns = 1,
            });

    // ── The daily interaction limit ──────────────────────────────────────────

    private void SetTurns(int mine, int everyone)
    {
        _allowances.Setup(a => a.GetDailyTurnsAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mine);
        _allowances.Setup(a => a.GetTenantDailyTurnsAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(everyone);
    }

    private void SetDailyLimits(int? perUser = null, int? perTenant = null)
    {
        _allowances.Setup(a => a.GetDailyLimitsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiDailyLimits { UserDailyTurns = perUser, TenantDailyTurns = perTenant });
    }

    private void UseSharedLogin(string? sessionId)
    {
        _accountGuard.Setup(g => g.IsAccountLocked(It.IsAny<ICurrentPrincipal>())).Returns(true);
        _principal.SetupGet(p => p.SessionId).Returns(sessionId);
    }

    [Fact]
    public async Task NoDailyLimit_ReadsNoCounters()
    {
        // Every workspace whose plan leaves this unlimited must not pay for a query per turn.
        GrantAllowance(null);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeTrue();
        decision.DailyTurnLimit.Should().BeNull();
        _allowances.Verify(a => a.GetDailyTurnsAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnderTheDailyLimit_ReportsWhatIsLeft()
    {
        GrantAllowance(null);
        SetDailyLimits(15);
        _allowances.Setup(a => a.GetDailyTurnsAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(8);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeTrue();
        // The panel counts down from these two.
        decision.DailyTurnLimit.Should().Be(15);
        decision.UsedTurnsToday.Should().Be(8);
    }

    [Fact]
    public async Task AtTheDailyLimit_IsDenied()
    {
        GrantAllowance(null);
        SetDailyLimits(10);
        _allowances.Setup(a => a.GetDailyTurnsAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("daily_limit_reached");
    }

    [Fact]
    public async Task TheWorkspaceCeilingStopsEveryoneEvenWithTurnsLeftPersonally()
    {
        SetDailyLimits(perUser: 20, perTenant: 100);
        SetTurns(mine: 2, everyone: 100);
        GrantAllowance(null);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeFalse();
        // A distinct reason: waiting until tomorrow is the remedy for your own limit, but
        // this one is everybody at once and an administrator can raise it.
        decision.Reason.Should().Be("workspace_daily_limit_reached");
        decision.DailyTurnLimit.Should().Be(100);
        decision.UsedTurnsToday.Should().Be(100);
        decision.DailyLimitIsWorkspaceWide.Should().BeTrue();
    }

    [Fact]
    public async Task AWorkspaceOnlyLimitCountsDownBeforeItBites()
    {
        // The gap this closes: a workspace-wide limit used to report nothing at all until
        // the moment it stopped someone, so the panel showed no countdown and the refusal
        // arrived with no warning.
        SetDailyLimits(perTenant: 50);
        SetTurns(mine: 0, everyone: 30);
        GrantAllowance(null);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeTrue();
        decision.DailyTurnLimit.Should().Be(50);
        decision.UsedTurnsToday.Should().Be(30);
        decision.DailyLimitIsWorkspaceWide.Should().BeTrue();
    }

    [Fact]
    public async Task TheLimitWithLessHeadroomIsTheOneReported()
    {
        // 3 personal turns left against 40 for the workspace: counting down the workspace's
        // number would promise headroom the caller does not have.
        SetDailyLimits(perUser: 20, perTenant: 100);
        SetTurns(mine: 17, everyone: 60);
        GrantAllowance(null);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeTrue();
        decision.DailyTurnLimit.Should().Be(20);
        decision.UsedTurnsToday.Should().Be(17);
        decision.DailyLimitIsWorkspaceWide.Should().BeFalse();
    }

    [Fact]
    public async Task TheWorkspaceLimitIsReportedWhenItIsTheTighterOne()
    {
        // 18 personal turns left, but only 5 for the whole workspace.
        SetDailyLimits(perUser: 20, perTenant: 100);
        SetTurns(mine: 2, everyone: 95);
        GrantAllowance(null);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeTrue();
        decision.DailyTurnLimit.Should().Be(100);
        decision.UsedTurnsToday.Should().Be(95);
        decision.DailyLimitIsWorkspaceWide.Should().BeTrue();
    }

    [Fact]
    public async Task ASharedLoginCannotChangeTheDailyLimits()
    {
        // The route also requires the admin role, and the demo membership is seeded as an
        // editor — but that is one string in a migration, and what this protects is spend
        // on the workspace's own API key. Many people behind one password must not be able
        // to raise the ceiling that governs them all.
        UseSharedLogin("session-abc");
        var sut = CreateSut();

        await Assert.ThrowsAsync<AccountLockedException>(
            () => sut.SetDailyLimitsAsync(userDailyTurns: 500, tenantDailyTurns: null, actorUserId: null));

        _allowances.Verify(a => a.SetDailyLimitsAsync(
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ARejectedDailyLimitIsNotSaved()
    {
        // Zero would strand everyone with no way to tell why; removing access is what the
        // allowance table is for.
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => sut.SetDailyLimitsAsync(userDailyTurns: 0, tenantDailyTurns: null, actorUserId: null));

        _allowances.Verify(a => a.SetDailyLimitsAsync(
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ASharedLoginCountsPerSessionNotPerAccount()
    {
        // The public demo is one account for everybody, so the session is the only thing
        // that separates two visitors.
        GrantAllowance(null);
        SetDailyLimits(5);
        UseSharedLogin("session-abc");
        _allowances.Setup(a => a.GetDailyTurnsAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await CreateSut().EvaluateAsync();

        _allowances.Verify(a => a.GetDailyTurnsAsync("session-abc", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ASharedLoginWithNoSessionSharesOneBucket()
    {
        // Nothing separates these visitors, so they must not each get a fresh allowance.
        GrantAllowance(null);
        SetDailyLimits(5);
        UseSharedLogin(null);
        _allowances.Setup(a => a.GetDailyTurnsAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("daily_limit_reached");
        _allowances.Verify(a => a.GetDailyTurnsAsync(
            It.Is<string>(sub => sub != UserId.ToString()), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnAdminOnASharedLoginIsStillLimited()
    {
        // The bypass exists because an admin can replace the key. A password everybody has
        // is not that, and the demo membership being admin must not hand every visitor an
        // unlimited assistant.
        _authorization.SetupGet(a => a.IsAdmin).Returns(true);
        SetDailyLimits(3);
        UseSharedLogin("session-xyz");
        _allowances.Setup(a => a.GetDailyTurnsAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("daily_limit_reached");
    }

    [Fact]
    public async Task AnOrdinaryAdminKeepsTheirMonthlyBypassButNotTheDailyOne()
    {
        _authorization.SetupGet(a => a.IsAdmin).Returns(true);
        SetDailyLimits(50);
        _allowances.Setup(a => a.GetDailyTurnsAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        var decision = await CreateSut().EvaluateAsync();

        decision.Allowed.Should().BeTrue();
        decision.MonthlyTokenLimit.Should().BeNull();   // the monthly bypass stands
        decision.DailyTurnLimit.Should().Be(50);        // the daily count still applies
        decision.UsedTurnsToday.Should().Be(4);
    }

    [Fact]
    public async Task RecordingATurnCountsAgainstTheSessionForASharedLogin()
    {
        UseSharedLogin("session-abc");

        await CreateSut().RecordDailyTurnAsync();

        _allowances.Verify(a => a.RecordDailyTurnAsync("session-abc", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordingATurnCountsAgainstTheUserOtherwise()
    {
        await CreateSut().RecordDailyTurnAsync();

        _allowances.Verify(a => a.RecordDailyTurnAsync(UserId.ToString(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
