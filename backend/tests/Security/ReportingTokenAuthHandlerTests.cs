using System.Text.Encodings.Web;
using Api.Reporting.Auth;
using Api.Services.Reporting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Orkyo.Foundation.Tests.Security;

/// <summary>
/// The reporting API's rate limit partitions on <c>Identity.Name</c>. Only the API-token handler
/// set the name claim, so every BI tool behind one NAT shared a single IP bucket.
/// </summary>
public class ReportingTokenAuthHandlerTests
{
    [Fact]
    public async Task TheIdentityName_IsTheTokenId()
    {
        var record = new ReportingTokenRecord { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), TokenPrefix = "abcd1234" };
        var tokens = new Mock<IReportingTokenService>();
        tokens.Setup(t => t.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var scheme = new AuthenticationScheme(ReportingTokenAuthHandler.SchemeName, null, typeof(ReportingTokenAuthHandler));
        var monitor = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        monitor.Setup(m => m.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());
        var handler = new ReportingTokenAuthHandler(monitor.Object, NullLoggerFactory.Instance, UrlEncoder.Default,
            tokens.Object, new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {ReportingTokenService.TokenScheme}_abcd1234_secret";
        await handler.InitializeAsync(scheme, context);

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal!.Identity!.Name.Should().Be(record.Id.ToString());
    }
}
