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

    public AiAccessServiceTests()
    {
        // The happy path everywhere: entitled workspace, key present, ordinary member.
        _featureGate.Setup(g => g.IsEnabledAsync(FeatureKeys.AiAssistant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _credentials.Setup(c => c.IsConfiguredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _authorization.SetupGet(a => a.IsAdmin).Returns(false);
        _principal.SetupGet(p => p.UserId).Returns(UserId);
    }

    private AiAccessService CreateSut() => new(
        _allowances.Object, _credentials.Object, _featureGate.Object, _authorization.Object,
        _principal.Object, _tenantUsers.Object,
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

        await CreateSut().RecordUsageAsync(UserId, inputTokens: 120, outputTokens: 340);

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
}
