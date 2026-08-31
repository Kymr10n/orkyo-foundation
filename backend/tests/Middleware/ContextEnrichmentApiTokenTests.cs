using System.Security.Claims;
using Api.Middleware;
using Api.Models;
using Api.PlatformApi.Auth;
using Api.Security;
using Api.Services;
using Api.Services.PlatformApi;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Orkyo.Foundation.Tests.Middleware;

/// <summary>
/// The bridge that lets a write-capable API token reuse the tenant authorization the whole app
/// already enforces, instead of a second permission system beside it.
///
/// This is the highest-risk logic in the MCP feature: a token authenticates a program, so nothing
/// downstream can fall back on "a human clicked this". Every assertion here is about the boundary
/// holding — the right role, the right tenant, and no path to administration.
/// </summary>
public class ContextEnrichmentApiTokenTests
{
    private readonly Mock<ILogger<ContextEnrichmentMiddleware>> _mockLogger = new();
    private readonly Mock<IIdentityLinkService> _mockIdentityLinkService = new();
    private readonly Mock<ITenantUserService> _mockTenantUserService = new();
    private readonly Mock<IBreakGlassSessionStore> _mockBreakGlass = new();
    private readonly CurrentPrincipal _currentPrincipal = new();
    private readonly CurrentTenant _currentTenant = new();
    private readonly CurrentAuthorizationContext _currentAuthContext = new();

    private static TenantContext CreateTenantContext(Guid tenantId, string slug = "acme") => new()
    {
        TenantId = tenantId,
        TenantSlug = slug,
        TenantDbConnectionString = $"Host=localhost;Database=tenant_{slug}",
        Status = "active",
    };

    private static ApiAccessTokenRecord Token(Guid tenantId, string scopes) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = "nightly agent",
        TokenPrefix = "abcd1234",
        Scopes = scopes,
    };

    /// <summary>Builds the context an authenticated API-token request arrives in.</summary>
    private static HttpContext ContextFor(ApiAccessTokenRecord token, TenantContext tenant)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ApiAccessTokenContextKeys.TokenIdClaim, token.Id.ToString())],
                ApiAccessTokenAuthHandler.SchemeName,
                ApiAccessTokenContextKeys.TokenIdClaim,
                ClaimTypes.Role)),
        };
        context.Items[ApiAccessTokenContextKeys.TokenRecord] = token;
        context.Items["TenantContext"] = tenant;
        return context;
    }

    private Task InvokeMiddleware(HttpContext context) =>
        new ContextEnrichmentMiddleware(_ => Task.CompletedTask, _mockLogger.Object).InvokeAsync(
            context, _currentPrincipal, _currentTenant, _currentAuthContext,
            _mockIdentityLinkService.Object, _mockTenantUserService.Object,
            _mockBreakGlass.Object);

    [Fact]
    public async Task AWriteScopedToken_IsAnEditorInItsOwnTenant()
    {
        var tenantId = Guid.NewGuid();
        var tenant = CreateTenantContext(tenantId);

        await InvokeMiddleware(ContextFor(Token(tenantId, PlatformApiScopes.ScheduleWrite), tenant));

        _currentAuthContext.Role.Should().Be(TenantRole.Editor);
        _currentAuthContext.IsMember.Should().BeTrue();
        _currentAuthContext.CanEdit.Should().BeTrue();
    }

    [Fact]
    public async Task AReadOnlyToken_IsAViewerAndCannotEdit()
    {
        // The whole point of the scope split: this is what stops a read token from writing, and
        // it is enforced by the same CanEdit property the HTTP write gate uses.
        var tenantId = Guid.NewGuid();

        await InvokeMiddleware(ContextFor(
            Token(tenantId, PlatformApiScopes.ScheduleRead), CreateTenantContext(tenantId)));

        _currentAuthContext.Role.Should().Be(TenantRole.Viewer);
        _currentAuthContext.IsMember.Should().BeTrue();
        _currentAuthContext.CanEdit.Should().BeFalse();
    }

    [Fact]
    public async Task ATokenPresentedAgainstAnotherTenantsHost_GetsNoRole()
    {
        // Defence in depth. The endpoint filter rejects this too, but the authorization context
        // must not grant anything on its own — a future endpoint without that filter still fails
        // closed rather than serving another tenant's data.
        var token = Token(Guid.NewGuid(), PlatformApiScopes.ScheduleWrite);
        var otherTenant = CreateTenantContext(Guid.NewGuid(), "other");

        await InvokeMiddleware(ContextFor(token, otherTenant));

        _currentAuthContext.Role.Should().Be(TenantRole.None);
        _currentAuthContext.IsMember.Should().BeFalse();
    }

    [Fact]
    public async Task ATokenIsNeverASiteAdmin()
    {
        // Break-glass is a human escalation path. A credential that could take it would be a way
        // to reach every tenant with one leaked string.
        var tenantId = Guid.NewGuid();

        await InvokeMiddleware(ContextFor(
            Token(tenantId, PlatformApiScopes.ScheduleWrite), CreateTenantContext(tenantId)));

        _currentPrincipal.IsSiteAdmin.Should().BeFalse();
        _currentAuthContext.IsAdmin.Should().BeFalse();
        _mockBreakGlass.Verify(
            s => s.HasActiveSession(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ATokenPrincipalIsIdentifiedByTheTokenItself()
    {
        // The token id doubles as the user id so an audit row names the exact credential that made
        // the change — the useful answer when the actor is a program.
        var tenantId = Guid.NewGuid();
        var token = Token(tenantId, PlatformApiScopes.ScheduleWrite);

        await InvokeMiddleware(ContextFor(token, CreateTenantContext(tenantId)));

        _currentPrincipal.UserId.Should().Be(token.Id);
        _currentPrincipal.IsAuthenticated.Should().BeTrue();
        _currentPrincipal.DisplayName.Should().Contain(token.Name);
    }

    [Fact]
    public async Task ATokenPrincipalNeverGoesThroughTheIdentityLinkLookup()
    {
        // There is no Keycloak subject behind a token; querying for one would either fail or, worse,
        // match some unrelated row.
        var tenantId = Guid.NewGuid();

        await InvokeMiddleware(ContextFor(
            Token(tenantId, PlatformApiScopes.ScheduleWrite), CreateTenantContext(tenantId)));

        _mockIdentityLinkService.Verify(
            s => s.FindByExternalIdentityAsync(It.IsAny<AuthProvider>(), It.IsAny<string>()),
            Times.Never);
        _mockIdentityLinkService.Verify(
            s => s.GetUserTenantRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ATokenGetsAUserStubSoAuditedWritesHaveAnActorRowToPointAt()
    {
        // tenant audit_events.actor_user_id has a foreign key to users(id). Without the stub, the
        // first audited mutation an agent makes fails on insert.
        var tenantId = Guid.NewGuid();
        var token = Token(tenantId, PlatformApiScopes.ScheduleWrite);

        await InvokeMiddleware(ContextFor(token, CreateTenantContext(tenantId)));

        _mockTenantUserService.Verify(s => s.CreateUserStubInTenantDatabaseAsync(
            It.Is<OrgContext>(o => o.OrgId == tenantId),
            token.Id,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ATokenDeniedItsTenantGetsNoUserStub()
    {
        // No membership, no stub: a cross-tenant token must not leave a row in a database it was
        // never entitled to reach.
        await InvokeMiddleware(ContextFor(
            Token(Guid.NewGuid(), PlatformApiScopes.ScheduleWrite),
            CreateTenantContext(Guid.NewGuid(), "other")));

        _mockTenantUserService.Verify(s => s.CreateUserStubInTenantDatabaseAsync(
            It.IsAny<OrgContext>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnApiSchemeRequestWithNoTokenRecord_FallsThroughToAnonymous()
    {
        // A malformed pipeline (scheme set, record missing) must not produce a privileged context.
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ApiAccessTokenContextKeys.TokenIdClaim, Guid.NewGuid().ToString())],
                ApiAccessTokenAuthHandler.SchemeName,
                ApiAccessTokenContextKeys.TokenIdClaim,
                ClaimTypes.Role)),
        };
        context.Items["TenantContext"] = CreateTenantContext(Guid.NewGuid());

        await InvokeMiddleware(context);

        _currentPrincipal.IsAuthenticated.Should().BeFalse();
        _currentAuthContext.IsMember.Should().BeFalse();
    }
}
