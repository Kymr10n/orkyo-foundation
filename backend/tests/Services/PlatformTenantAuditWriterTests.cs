using System.Collections.Generic;
using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// Unit tests for <see cref="PlatformTenantAuditWriter"/> — writes a platform-context audit event into a
/// TARGET tenant's own database, labeled <c>source=platform</c> with the actor denormalized into metadata
/// (and <c>actor_user_id</c> NULL, since platform staff aren't tenant-DB users). Best-effort.
/// </summary>
public class PlatformTenantAuditWriterTests
{
    private readonly Mock<ITenantResolver> _resolver = new();
    private readonly Mock<ITenantUserService> _tenantUsers = new();
    private const string Slug = "acme";

    private PlatformTenantAuditWriter Writer() =>
        new(_resolver.Object, _tenantUsers.Object, NullLogger<PlatformTenantAuditWriter>.Instance);

    private void SetupResolver(Guid tenantId) =>
        _resolver.Setup(r => r.ResolveTenantAsync(null, Slug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantContext
            {
                TenantId = tenantId,
                TenantSlug = Slug,
                TenantDbConnectionString = "Host=localhost;Database=tenant_acme",
                Status = "active",
            });

    private void VerifyNoWrite() => _tenantUsers.Verify(s => s.RecordAuditEventAsync(
        It.IsAny<OrgContext>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
        It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task WriteAsync_RecordsIntoTargetTenantDb_LabeledPlatform_ActorDenormalized()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        SetupResolver(tenantId);

        IReadOnlyDictionary<string, object?>? captured = null;
        _tenantUsers.Setup(s => s.RecordAuditEventAsync(
                It.IsAny<OrgContext>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback<OrgContext, string, Guid?, string?, string?, object?, CancellationToken>(
                (_, _, _, _, _, meta, _) => captured = meta as IReadOnlyDictionary<string, object?>)
            .Returns(Task.CompletedTask);

        await Writer().WriteAsync(
            Slug, "break_glass.granted", actorId, "staff@orkyo.com",
            targetType: "tenant", targetId: null,
            metadata: new Dictionary<string, object?> { ["reason"] = "investigating" });

        // Written into the target tenant's DB, actor_user_id NULL (platform staff not a tenant-DB user).
        _tenantUsers.Verify(s => s.RecordAuditEventAsync(
            It.Is<OrgContext>(o => o.OrgId == tenantId), "break_glass.granted", null,
            "tenant", null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Metadata labels the row as platform and denormalizes the actor, merging caller extras.
        Assert.NotNull(captured);
        Assert.Equal("platform", captured!["source"]);
        Assert.Equal(actorId.ToString(), captured["actorUserId"]);
        Assert.Equal("staff@orkyo.com", captured["actorEmail"]);
        Assert.Equal("investigating", captured["reason"]);
    }

    [Fact]
    public async Task WriteAsync_NullMetadata_StillWritesPlatformFields()
    {
        var tenantId = Guid.NewGuid();
        SetupResolver(tenantId);

        await Writer().WriteAsync(Slug, "membership.added", Guid.NewGuid(), "staff@orkyo.com");

        _tenantUsers.Verify(s => s.RecordAuditEventAsync(
            It.Is<OrgContext>(o => o.OrgId == tenantId), "membership.added", null,
            null, null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WriteAsync_UnresolvedTenant_DoesNotWrite()
    {
        // resolver returns null (not set up) → nothing to write to.
        await Writer().WriteAsync("ghost", "tenant.updated", Guid.NewGuid(), "staff@orkyo.com");
        VerifyNoWrite();
    }

    [Fact]
    public async Task WriteAsync_ResolverThrows_IsSwallowed_BestEffort()
    {
        _resolver.Setup(r => r.ResolveTenantAsync(null, Slug, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("control-plane down"));

        // Best-effort: auditing must never break the platform operation it documents.
        var act = async () => await Writer().WriteAsync(Slug, "tenant.tier_changed", Guid.NewGuid(), "staff@orkyo.com");

        await act.Should().NotThrowAsync();
        VerifyNoWrite();
    }
}
