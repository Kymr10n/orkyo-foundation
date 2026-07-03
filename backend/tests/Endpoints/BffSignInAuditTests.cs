using Api.Models;
using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Unit tests for <see cref="SignInAuditRecorder"/> — records <c>session.signed_in</c> into the
/// tenant's OWN database on sign-in, attributed to the (single-tenant) user. Invoked from the shared
/// <c>BffSessionEstablisher.EstablishAsync</c> seam so every login path (OIDC callback + demo login)
/// is covered.
/// </summary>
public class BffSignInAuditTests
{
    private readonly Mock<ISessionService> _session = new();
    private readonly Mock<ITenantResolver> _resolver = new();
    private readonly Mock<ITenantUserService> _tenantUsers = new();
    private readonly Guid _userId = Guid.NewGuid();
    private const string Slug = "acme";

    private SignInAuditRecorder Recorder() =>
        new(_session.Object, _resolver.Object, _tenantUsers.Object, NullLogger<SignInAuditRecorder>.Instance);

    private void SetupTenants(params Guid[] tenantIds)
    {
        var resp = new SessionBootstrapResponse
        {
            User = new UserInfo { Id = _userId, Email = "u@test.com", DisplayName = "U" },
            Tenants = tenantIds.Select(id => new TenantMembershipInfo
            {
                TenantId = id,
                Slug = Slug,
                DisplayName = "T",
                Role = "admin",
                State = "active",
                Tier = "professional",
            }).ToList(),
        };
        _session.Setup(s => s.BuildSessionResponseAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resp);
    }

    private void SetupResolver(Guid tenantId) =>
        _resolver.Setup(r => r.ResolveTenantAsync(null, Slug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantContext
            {
                TenantId = tenantId,
                TenantSlug = Slug,
                TenantDbConnectionString = "Host=localhost;Database=tenant_acme",
                Status = "active",
            });

    private Task Invoke(string? email = "u@test.com") =>
        Recorder().RecordAsync(_userId, email);

    private void VerifyNoAudit() => _tenantUsers.Verify(s => s.RecordAuditEventAsync(
        It.IsAny<OrgContext>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
        It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task SingleTenant_RecordsSignInIntoTenantDb_AttributedToUser()
    {
        var tenantId = Guid.NewGuid();
        SetupTenants(tenantId);
        SetupResolver(tenantId);

        await Invoke();

        // Stub ensured (FK), then event attributed to the user in the tenant's own DB.
        _tenantUsers.Verify(s => s.CreateUserStubInTenantDatabaseAsync(
            It.Is<OrgContext>(o => o.OrgId == tenantId), _userId, "u@test.com", It.IsAny<CancellationToken>()), Times.Once);
        _tenantUsers.Verify(s => s.RecordAuditEventAsync(
            It.Is<OrgContext>(o => o.OrgId == tenantId), "session.signed_in", _userId,
            "tenant", tenantId.ToString(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MultipleTenants_DoesNotRecord()
    {
        SetupTenants(Guid.NewGuid(), Guid.NewGuid());
        await Invoke();
        VerifyNoAudit();
    }

    [Fact]
    public async Task NoTenants_DoesNotRecord()
    {
        SetupTenants();
        await Invoke();
        VerifyNoAudit();
    }

    [Fact]
    public async Task UnresolvedTenant_DoesNotRecord()
    {
        SetupTenants(Guid.NewGuid());
        // resolver returns null (not set up)
        await Invoke();
        VerifyNoAudit();
    }
}
