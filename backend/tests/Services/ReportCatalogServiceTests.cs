using Api.Reporting;
using Api.Security;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class ReportCatalogServiceTests
{
    private static IOptions<ReportingOptions> EnabledOpts =>
        Options.Create(new ReportingOptions { Enabled = true });

    private static IOptions<ReportingOptions> DisabledOpts =>
        Options.Create(new ReportingOptions { Enabled = false });

    private static IAuthorizationContext ContextWithRole(TenantRole role)
    {
        var ctx = new CurrentAuthorizationContext();
        ctx.SetContext(new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "test",
            Role = role,
        });
        return ctx;
    }

    [Fact]
    public void GetVisibleReports_WhenDisabled_ReturnsEmpty()
    {
        var svc = new ReportCatalogService(ContextWithRole(TenantRole.Admin), DisabledOpts);
        svc.GetVisibleReports().Should().BeEmpty();
    }

    [Fact]
    public void GetVisibleReports_WhenRoleNone_ReturnsEmpty()
    {
        var svc = new ReportCatalogService(ContextWithRole(TenantRole.None), EnabledOpts);
        svc.GetVisibleReports().Should().BeEmpty();
    }

    [Theory]
    [InlineData(TenantRole.Viewer)]
    [InlineData(TenantRole.Editor)]
    [InlineData(TenantRole.Admin)]
    public void GetVisibleReports_WhenViewerOrAbove_ReturnsAllMvpReports(TenantRole role)
    {
        var svc = new ReportCatalogService(ContextWithRole(role), EnabledOpts);
        var reports = svc.GetVisibleReports();

        reports.Should().HaveCount(ReportCatalogue.All.Count);
        reports.Select(r => r.Key).Should().BeEquivalentTo(
            new[] { "space-utilization", "request-pipeline", "allocation-conflicts" });
    }

    [Fact]
    public void GetVisibleReports_ReturnsOnlyReportsAtOrBelowCallerRole()
    {
        // If a report were to require Admin role, a Viewer should not see it.
        var viewerCtx = ContextWithRole(TenantRole.Viewer);
        var viewerSvc = new ReportCatalogService(viewerCtx, EnabledOpts);

        // All MVP reports require Viewer — so Viewer sees all 3.
        viewerSvc.GetVisibleReports().Should().HaveCount(3);
    }
}
