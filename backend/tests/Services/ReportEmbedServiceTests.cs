using Api.Helpers;
using Api.Integrations.Reporting;
using Api.Reporting;
using Api.Security;
using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class ReportEmbedServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DashboardUuid = Guid.NewGuid();
    private const string ReportKey = "space-utilization";
    private const string FakeToken = "superset-guest-token-abc";

    private static IAuthorizationContext ViewerContext()
    {
        var ctx = new CurrentAuthorizationContext();
        ctx.SetContext(new AuthorizationContext
        {
            TenantId = TenantId,
            TenantSlug = "test",
            Role = TenantRole.Viewer,
        });
        return ctx;
    }

    private static IOptions<ReportingOptions> Opts(bool enabled = true) =>
        Options.Create(new ReportingOptions
        {
            Enabled = enabled,
            BaseUrl = "https://superset.test",
            EmbedTokenTtlSeconds = 300,
        });

    [Fact]
    public async Task CreateEmbedTokenAsync_WhenDisabled_ThrowsFeatureNotAvailable()
    {
        var svc = BuildService(Opts(enabled: false));
        await Assert.ThrowsAsync<FeatureNotAvailableException>(
            () => svc.CreateEmbedTokenAsync(ReportKey));
    }

    [Fact]
    public async Task CreateEmbedTokenAsync_WhenUnknownKey_ThrowsNotFoundException()
    {
        var svc = BuildService();
        await Assert.ThrowsAsync<NotFoundException>(
            () => svc.CreateEmbedTokenAsync("non-existent-report"));
    }

    [Fact]
    public async Task CreateEmbedTokenAsync_WhenRoleInsufficient_ThrowsUnauthorized()
    {
        var noneCtx = new CurrentAuthorizationContext();
        noneCtx.SetContext(new AuthorizationContext
        {
            TenantId = TenantId,
            TenantSlug = "test",
            Role = TenantRole.None,
        });

        var svc = BuildService(authCtx: noneCtx);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.CreateEmbedTokenAsync(ReportKey));
    }

    [Fact]
    public async Task CreateEmbedTokenAsync_WhenBindingMissing_ThrowsFeatureNotAvailable()
    {
        var svc = BuildService(bindingMissing: true);
        await Assert.ThrowsAsync<FeatureNotAvailableException>(
            () => svc.CreateEmbedTokenAsync(ReportKey));
    }

    [Fact]
    public async Task CreateEmbedTokenAsync_HappyPath_ReturnsTokenAndAudits()
    {
        var auditMock = new Mock<IAdminAuditService>();
        var svc = BuildService(audit: auditMock.Object);

        var result = await svc.CreateEmbedTokenAsync(ReportKey);

        result.ReportKey.Should().Be(ReportKey);
        result.Token.Should().Be(FakeToken);
        result.EmbedUrl.Should().Contain(DashboardUuid.ToString());
        result.ExpiresAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(300), TimeSpan.FromSeconds(5));

        auditMock.Verify(a => a.RecordEventAsync(
            It.IsAny<Guid?>(),
            "report.embed_token.issued",
            "report",
            ReportKey,
            It.IsAny<object?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateEmbedTokenAsync_TenantAlwaysComeFromSession_NotFromBody()
    {
        // Engine is called with the dashboard UUID for the session's TenantId.
        // If the service accepted an external tenant ID, a different UUID would be returned.
        var engineMock = new Mock<IReportingEngineClient>();
        engineMock
            .Setup(e => e.CreateGuestTokenAsync(DashboardUuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FakeToken);

        var svc = BuildService(engine: engineMock.Object);
        await svc.CreateEmbedTokenAsync(ReportKey);

        engineMock.Verify(
            e => e.CreateGuestTokenAsync(DashboardUuid, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Builder ───────────────────────────────────────────────────────────────

    private ReportEmbedService BuildService(
        IOptions<ReportingOptions>? opts = null,
        IAuthorizationContext? authCtx = null,
        IAdminAuditService? audit = null,
        IReportingEngineClient? engine = null,
        Guid? dashboardUuid = null,
        bool bindingMissing = false)
    {
        opts ??= Opts();
        authCtx ??= ViewerContext();

        var tenantMock = new Mock<ICurrentTenant>();
        tenantMock.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var principalMock = new Mock<ICurrentPrincipal>();
        principalMock.Setup(p => p.UserId).Returns(Guid.NewGuid());

        var engineMock = engine ?? BuildEngineMock();
        var auditSvc = audit ?? Mock.Of<IAdminAuditService>();

        var bindingsMock = new Mock<IReportBindingRepository>();
        bindingsMock
            .Setup(r => r.GetDashboardUuidAsync(TenantId, ReportKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bindingMissing ? (Guid?)null : (dashboardUuid ?? DashboardUuid));

        return new ReportEmbedService(
            authCtx,
            tenantMock.Object,
            principalMock.Object,
            bindingsMock.Object,
            engineMock,
            auditSvc,
            opts,
            NullLogger<ReportEmbedService>.Instance);
    }

    private static IReportingEngineClient BuildEngineMock()
    {
        var mock = new Mock<IReportingEngineClient>();
        mock.Setup(e => e.CreateGuestTokenAsync(DashboardUuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FakeToken);
        return mock.Object;
    }
}
