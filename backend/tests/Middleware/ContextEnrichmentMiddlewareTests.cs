using System.Security.Claims;
using Api.Integrations.Keycloak;
using Api.Middleware;
using Api.Models;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Orkyo.Foundation.Tests.Middleware;

public class ContextEnrichmentMiddlewareTests
{
    private readonly Mock<ILogger<ContextEnrichmentMiddleware>> _mockLogger = new();
    private readonly Mock<IIdentityLinkService> _mockIdentityLinkService = new();
    private readonly Mock<ITenantUserService> _mockTenantUserService = new();
    private readonly Mock<IAdminAuditService> _mockAuditService = new();
    private readonly Mock<IBreakGlassSessionStore> _mockBreakGlass = new();
    private readonly CurrentPrincipal _currentPrincipal = new();
    private readonly CurrentTenant _currentTenant = new();
    private readonly CurrentAuthorizationContext _currentAuthContext = new();

    public ContextEnrichmentMiddlewareTests()
    {
        // Default: no active break-glass session
        _mockBreakGlass.Setup(s => s.HasActiveSession(It.IsAny<Guid>(), It.IsAny<string>())).Returns(false);
        _mockAuditService.Setup(s => s.RecordEventAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<object?>())).Returns(Task.CompletedTask);
    }

    private ContextEnrichmentMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, _mockLogger.Object);

    private static HttpContext CreateHttpContext(ClaimsPrincipal? user = null, TenantContext? tenantContext = null)
    {
        var context = new DefaultHttpContext();
        if (user != null) context.User = user;
        if (tenantContext != null) context.Items["TenantContext"] = tenantContext;
        return context;
    }

    private static ClaimsPrincipal CreateLegacyUser(Guid userId, string email = "test@example.com", string? name = "Test User")
    {
        var claims = new List<Claim> { new("user_id", userId.ToString()), new(ClaimTypes.Email, email) };
        if (name != null) claims.Add(new(ClaimTypes.Name, name));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
    }

    private static ClaimsPrincipal CreateKeycloakUser(string subject, string? email = "kc@example.com", string? name = "KC User", bool isSiteAdmin = false)
    {
        var claims = new List<Claim> { new("sub", subject) };
        if (email != null) { claims.Add(new(ClaimTypes.Email, email)); claims.Add(new("email", email)); }
        if (name != null) { claims.Add(new(ClaimTypes.Name, name)); claims.Add(new("preferred_username", name)); }
        if (isSiteAdmin) claims.Add(new("realm_access", "{\"roles\":[\"user\",\"site-admin\"]}"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static TenantContext CreateTenantContext(Guid? tenantId = null, string slug = "test") =>
        new()
        {
            TenantId = tenantId ?? Guid.NewGuid(),
            TenantSlug = slug,
            TenantDbConnectionString = $"Host=localhost;Database=tenant_{slug}",
            Tier = ServiceTier.Free,
            Status = "active"
        };

    private Task InvokeMiddleware(HttpContext context) =>
        CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(
            context, _currentPrincipal, _currentTenant, _currentAuthContext,
            _mockIdentityLinkService.Object, _mockTenantUserService.Object,
            _mockBreakGlass.Object, _mockAuditService.Object);

    [Fact]
    public async Task AnonymousUser_SetsPrincipalToAnonymous()
    {
        await InvokeMiddleware(CreateHttpContext());
        _currentPrincipal.IsAuthenticated.Should().BeFalse();
        _currentPrincipal.UserId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task AnonymousUserWithTenant_SetsOnlyTenantContext()
    {
        var tenant = CreateTenantContext();
        await InvokeMiddleware(CreateHttpContext(tenantContext: tenant));
        _currentPrincipal.IsAuthenticated.Should().BeFalse();
        _currentTenant.TenantId.Should().Be(tenant.TenantId);
    }

    [Fact]
    public async Task LegacyUserNoTenant_SetsPrincipalOnly()
    {
        var userId = Guid.NewGuid();
        await InvokeMiddleware(CreateHttpContext(user: CreateLegacyUser(userId)));
        _currentPrincipal.IsAuthenticated.Should().BeTrue();
        _currentPrincipal.UserId.Should().Be(userId);
        _currentPrincipal.GetContext().AuthProvider.Should().Be(AuthProvider.Local);
    }

    [Fact]
    public async Task LegacyUserWithTenant_ResolvesAuthorizationContext()
    {
        var userId = Guid.NewGuid();
        var tenant = CreateTenantContext();
        _mockIdentityLinkService.Setup(s => s.GetUserTenantRoleAsync(userId, tenant.TenantId)).ReturnsAsync(TenantRole.Admin);
        _mockTenantUserService.Setup(s => s.CreateUserStubInTenantDatabaseAsync(It.IsAny<OrgContext>(), userId, "test@example.com")).Returns(Task.CompletedTask);

        await InvokeMiddleware(CreateHttpContext(user: CreateLegacyUser(userId), tenantContext: tenant));

        _currentAuthContext.Role.Should().Be(TenantRole.Admin);
        _currentAuthContext.IsMember.Should().BeTrue();
        _mockTenantUserService.Verify(s => s.CreateUserStubInTenantDatabaseAsync(It.IsAny<OrgContext>(), userId, "test@example.com"), Times.Once);
    }

    [Fact]
    public async Task LegacyUserWithTenant_NonMember_DoesNotCreateStub()
    {
        var userId = Guid.NewGuid();
        var tenant = CreateTenantContext();
        _mockIdentityLinkService.Setup(s => s.GetUserTenantRoleAsync(userId, tenant.TenantId)).ReturnsAsync(TenantRole.None);

        await InvokeMiddleware(CreateHttpContext(user: CreateLegacyUser(userId), tenantContext: tenant));

        _currentAuthContext.IsMember.Should().BeFalse();
        _mockTenantUserService.Verify(s => s.CreateUserStubInTenantDatabaseAsync(It.IsAny<OrgContext>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LegacyUserInvalidUserId_ReturnsAnonymous()
    {
        var claims = new List<Claim> { new("user_id", "not-a-guid"), new(ClaimTypes.Email, "invalid@example.com") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
        await InvokeMiddleware(CreateHttpContext(user: user));
        _currentPrincipal.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task KeycloakUserLinked_ResolvesFullPrincipal()
    {
        var subject = "kc-subject-123";
        var internalUserId = Guid.NewGuid();
        var linkedPrincipal = new PrincipalContext
        {
            UserId = internalUserId,
            Email = "linked@example.com",
            DisplayName = "Linked User",
            AuthProvider = AuthProvider.Keycloak,
            ExternalSubject = subject
        };
        _mockIdentityLinkService.Setup(s => s.FindByExternalIdentityAsync(AuthProvider.Keycloak, subject)).ReturnsAsync(linkedPrincipal);

        await InvokeMiddleware(CreateHttpContext(user: CreateKeycloakUser(subject)));

        _currentPrincipal.UserId.Should().Be(internalUserId);
        _currentPrincipal.GetContext().AuthProvider.Should().Be(AuthProvider.Keycloak);
    }

    [Fact]
    public async Task KeycloakUserNotLinked_ReturnsPartialPrincipal()
    {
        var subject = "kc-subject-unlinked";
        _mockIdentityLinkService.Setup(s => s.FindByExternalIdentityAsync(AuthProvider.Keycloak, subject)).ReturnsAsync((PrincipalContext?)null);

        await InvokeMiddleware(CreateHttpContext(user: CreateKeycloakUser(subject, email: "unlinked@example.com")));

        _currentPrincipal.UserId.Should().Be(Guid.Empty);
        _currentPrincipal.GetContext().AuthProvider.Should().Be(AuthProvider.Keycloak);
    }

    [Fact]
    public async Task SiteAdminWithActiveSession_GetsBreakGlassAdminAccess()
    {
        var subject = "kc-site-admin-123";
        var internalUserId = Guid.NewGuid();
        var tenant = CreateTenantContext();
        var linkedPrincipal = new PrincipalContext
        {
            UserId = internalUserId,
            Email = "siteadmin@example.com",
            DisplayName = "Site Admin",
            AuthProvider = AuthProvider.Keycloak,
            ExternalSubject = subject,
            IsSiteAdmin = true
        };
        _mockIdentityLinkService.Setup(s => s.FindByExternalIdentityAsync(AuthProvider.Keycloak, subject)).ReturnsAsync(linkedPrincipal);
        _mockBreakGlass.Setup(s => s.HasActiveSession(internalUserId, tenant.TenantSlug)).Returns(true);

        await InvokeMiddleware(CreateHttpContext(user: CreateKeycloakUser(subject, isSiteAdmin: true), tenantContext: tenant));

        _currentPrincipal.IsSiteAdmin.Should().BeTrue();
        _currentAuthContext.Role.Should().Be(TenantRole.Admin);
        _mockIdentityLinkService.Verify(s => s.GetUserTenantRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        _mockAuditService.Verify(s => s.RecordEventAsync(internalUserId, "break_glass_access", "tenant", tenant.TenantId.ToString(), It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task SiteAdminWithoutSession_NotMember_GetsNoTenantAccess()
    {
        var subject = "kc-site-admin-no-session";
        var internalUserId = Guid.NewGuid();
        var tenant = CreateTenantContext();
        var linkedPrincipal = new PrincipalContext
        {
            UserId = internalUserId,
            Email = "siteadmin@example.com",
            AuthProvider = AuthProvider.Keycloak,
            ExternalSubject = subject,
            IsSiteAdmin = true
        };
        _mockIdentityLinkService.Setup(s => s.FindByExternalIdentityAsync(AuthProvider.Keycloak, subject)).ReturnsAsync(linkedPrincipal);

        await InvokeMiddleware(CreateHttpContext(user: CreateKeycloakUser(subject, isSiteAdmin: true), tenantContext: tenant));

        _currentAuthContext.Role.Should().Be(TenantRole.None);
        _currentAuthContext.IsMember.Should().BeFalse();
    }

    [Fact]
    public async Task ClearCache_AllowsSubsequentDbQueries()
    {
        ContextEnrichmentMiddleware.ClearCache();
        var subject = $"kc-clear-{Guid.NewGuid()}";
        var userId = Guid.NewGuid();
        var principal = new PrincipalContext
        {
            UserId = userId,
            Email = "kc@example.com",
            AuthProvider = AuthProvider.Keycloak,
            ExternalSubject = subject
        };
        _mockIdentityLinkService.Setup(s => s.FindByExternalIdentityAsync(AuthProvider.Keycloak, subject)).ReturnsAsync(principal);

        var tenant = CreateTenantContext();
        var user = CreateKeycloakUser(subject);
        var middleware = new ContextEnrichmentMiddleware(_ => Task.CompletedTask, _mockLogger.Object);

        await middleware.InvokeAsync(CreateHttpContext(user, tenant), new CurrentPrincipal(), new CurrentTenant(), new CurrentAuthorizationContext(),
            _mockIdentityLinkService.Object, _mockTenantUserService.Object, _mockBreakGlass.Object, _mockAuditService.Object);

        ContextEnrichmentMiddleware.ClearCache();

        await middleware.InvokeAsync(CreateHttpContext(user, tenant), new CurrentPrincipal(), new CurrentTenant(), new CurrentAuthorizationContext(),
            _mockIdentityLinkService.Object, _mockTenantUserService.Object, _mockBreakGlass.Object, _mockAuditService.Object);

        _mockIdentityLinkService.Verify(s => s.FindByExternalIdentityAsync(AuthProvider.Keycloak, subject), Times.Exactly(2));
    }
}
