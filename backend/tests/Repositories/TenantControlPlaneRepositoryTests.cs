using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// DB-backed tests for <see cref="TenantControlPlaneRepository"/> — the control-plane
/// tenant/membership seam that replaced the per-query contract/factory/flow triplets in
/// 0.8.0. Ports the triplet flows' behavior contracts (not-found → null, NULL owner
/// handling, no-rows scalar semantics, membership ordering) onto the repository.
/// </summary>
[Collection("Database collection")]
public class TenantControlPlaneRepositoryTests
{
    private readonly ITenantControlPlaneRepository _repo;
    private readonly IDbConnectionFactory _connectionFactory;

    public TenantControlPlaneRepositoryTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _repo = scope.ServiceProvider.GetRequiredService<ITenantControlPlaneRepository>();
        _connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
    }

    // ── Seed helpers ─────────────────────────────────────────────────────────

    private static string UniqueSlug(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private async Task<Guid> CreateUserAsync()
    {
        var id = Guid.NewGuid();
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO users (id, email, display_name, status, created_at, updated_at)
            VALUES (@id, @email, 'Repo Test User', 'active', NOW(), NOW())",
            p =>
            {
                p.AddWithValue("id", id);
                p.AddWithValue("email", $"{id:N}@repo.test");
            });
        return id;
    }

    private async Task<Guid> SeedTenantAsync(
        string? slug = null, string status = "active", Guid? ownerUserId = null)
    {
        var id = Guid.NewGuid();
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO tenants (id, slug, display_name, status, db_identifier, owner_user_id, created_at, updated_at)
            VALUES (@id, @slug, 'Repo Test Tenant', @status, @dbId, @owner, NOW(), NOW())",
            p =>
            {
                p.AddWithValue("id", id);
                p.AddWithValue("slug", slug ?? UniqueSlug("repo"));
                p.AddWithValue("status", status);
                p.AddWithValue("dbId", $"tenant_repo_{id:N}"[..40]);
                p.AddNullable("owner", ownerUserId);
            });
        return id;
    }

    private async Task SeedMembershipAsync(
        Guid tenantId, Guid userId, string role = "admin", string status = "active", DateTime? createdAt = null)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO tenant_memberships (user_id, tenant_id, role, status, created_at, updated_at)
            VALUES (@userId, @tenantId, @role, @status, COALESCE(@createdAt, NOW()), NOW())",
            p =>
            {
                p.AddWithValue("userId", userId);
                p.AddWithValue("tenantId", tenantId);
                p.AddWithValue("role", role);
                p.AddWithValue("status", status);
                p.AddNullable("createdAt", createdAt);
            });
    }

    private async Task<T> QueryScalarAsync<T>(string sql, Guid id)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.ExecuteScalarAsync<T>(sql, p => p.AddWithValue("id", id));
    }

    // ── OwnsActiveTenantAsync (ports TenantOwnershipEligibility flow) ────────

    [Fact]
    public async Task OwnsActiveTenant_False_WhenUserOwnsNothing()
    {
        var userId = await CreateUserAsync();

        (await _repo.OwnsActiveTenantAsync(userId)).Should().BeFalse("no rows → count 0");
    }

    [Fact]
    public async Task OwnsActiveTenant_True_WhenUserOwnsAnActiveTenant()
    {
        var userId = await CreateUserAsync();
        await SeedTenantAsync(ownerUserId: userId);

        (await _repo.OwnsActiveTenantAsync(userId)).Should().BeTrue();
    }

    [Fact]
    public async Task OwnsActiveTenant_False_WhenOwnedTenantIsDeleting()
    {
        var userId = await CreateUserAsync();
        await SeedTenantAsync(status: "deleting", ownerUserId: userId);

        (await _repo.OwnsActiveTenantAsync(userId)).Should().BeFalse("deleting tenants don't block re-creation");
    }

    // ── IsSlugTakenAsync (ports TenantSlugAvailability flow) ─────────────────

    [Fact]
    public async Task IsSlugTaken_False_ForUnknownSlug()
    {
        (await _repo.IsSlugTakenAsync(UniqueSlug("nope"))).Should().BeFalse("no row → scalar null → available");
    }

    [Fact]
    public async Task IsSlugTaken_True_ForExistingSlug()
    {
        var slug = UniqueSlug("taken");
        await SeedTenantAsync(slug: slug);

        (await _repo.IsSlugTakenAsync(slug)).Should().BeTrue();
    }

    // ── CreateTenantWithOwnerAsync (ports TenantCreation contract) ───────────

    [Fact]
    public async Task CreateTenantWithOwner_ReturnsRecord_AndCreatesOwnerAdminMembership()
    {
        var ownerId = await CreateUserAsync();
        var slug = UniqueSlug("create");

        var tenant = await _repo.CreateTenantWithOwnerAsync(slug, "Created Tenant", $"tenant_{slug}", ownerId);

        tenant.Slug.Should().Be(slug);
        tenant.DisplayName.Should().Be("Created Tenant");
        tenant.Status.Should().Be("active");
        tenant.DbIdentifier.Should().Be($"tenant_{slug}");
        tenant.OwnerUserId.Should().Be(ownerId);

        var membership = await _repo.GetMembershipRoleStatusAsync(tenant.Id, ownerId);
        membership.Should().NotBeNull();
        membership!.Role.Should().Be("admin");
        membership.Status.Should().Be("active");
    }

    [Fact]
    public async Task CreateTenantWithOwner_Throws_OnDuplicateSlug()
    {
        var ownerId = await CreateUserAsync();
        var slug = UniqueSlug("dup");
        await SeedTenantAsync(slug: slug);

        var act = () => _repo.CreateTenantWithOwnerAsync(slug, "Dup", $"tenant_{slug}2", ownerId);

        await act.Should().ThrowAsync<PostgresException>();
    }

    // ── GetByIdAsync / GetBySlugAsync (ports TenantRecord reader flow) ───────

    [Fact]
    public async Task GetById_ReturnsNull_WhenMissing()
    {
        (await _repo.GetByIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetById_HandlesNullOwner()
    {
        var tenantId = await SeedTenantAsync(ownerUserId: null);

        var record = await _repo.GetByIdAsync(tenantId);

        record.Should().NotBeNull();
        record!.OwnerUserId.Should().BeNull("owner_user_id NULL must map to null, not throw");
    }

    [Fact]
    public async Task GetBySlug_RoundTrips()
    {
        var slug = UniqueSlug("byslug");
        var tenantId = await SeedTenantAsync(slug: slug);

        var record = await _repo.GetBySlugAsync(slug);

        record.Should().NotBeNull();
        record!.Id.Should().Be(tenantId);
        record.Slug.Should().Be(slug);
    }

    // ── UpdateDisplayNameAsync ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateDisplayName_ReturnsUpdatedRecord()
    {
        var tenantId = await SeedTenantAsync();

        var record = await _repo.UpdateDisplayNameAsync(tenantId, "Renamed");

        record.Should().NotBeNull();
        record!.DisplayName.Should().Be("Renamed");
    }

    [Fact]
    public async Task UpdateDisplayName_ReturnsNull_WhenTenantMissing()
    {
        (await _repo.UpdateDisplayNameAsync(Guid.NewGuid(), "Ghost")).Should().BeNull();
    }

    // ── GetOwnerStatusAsync (ports TenantOwnerStatus reader flow) ────────────

    [Fact]
    public async Task GetOwnerStatus_ReturnsNull_WhenTenantMissing()
    {
        (await _repo.GetOwnerStatusAsync(Guid.NewGuid())).Should().BeNull("not-found must be distinguishable");
    }

    [Fact]
    public async Task GetOwnerStatus_ReturnsNullOwner_AndStatus()
    {
        var tenantId = await SeedTenantAsync(status: "suspended", ownerUserId: null);

        var status = await _repo.GetOwnerStatusAsync(tenantId);

        status.Should().NotBeNull();
        status!.OwnerUserId.Should().BeNull();
        status.Status.Should().Be("suspended");
    }

    // ── GetMembershipRoleStatusAsync (ports role-status reader flow) ─────────

    [Fact]
    public async Task GetMembershipRoleStatus_ReturnsNull_WhenNoMembership()
    {
        var tenantId = await SeedTenantAsync();

        (await _repo.GetMembershipRoleStatusAsync(tenantId, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetMembershipRoleStatus_ReturnsRoleAndStatus_RegardlessOfStatus()
    {
        var tenantId = await SeedTenantAsync();
        var userId = await CreateUserAsync();
        await SeedMembershipAsync(tenantId, userId, role: "editor", status: "pending");

        var membership = await _repo.GetMembershipRoleStatusAsync(tenantId, userId);

        membership.Should().NotBeNull();
        membership!.Role.Should().Be("editor");
        membership.Status.Should().Be("pending");
    }

    // ── GetUserMembershipsAsync (ports membership-list reader flow) ──────────

    [Fact]
    public async Task GetUserMemberships_ReturnsJoinedRows_NewestFirst()
    {
        var userId = await CreateUserAsync();
        var ownerId = await CreateUserAsync();
        var olderTenant = await SeedTenantAsync(ownerUserId: ownerId);
        var newerTenant = await SeedTenantAsync(ownerUserId: null);
        await SeedMembershipAsync(olderTenant, userId, role: "editor", createdAt: DateTime.UtcNow.AddDays(-2));
        await SeedMembershipAsync(newerTenant, userId, role: "admin", createdAt: DateTime.UtcNow.AddDays(-1));

        var rows = await _repo.GetUserMembershipsAsync(userId);

        rows.Should().HaveCount(2);
        rows[0].TenantId.Should().Be(newerTenant, "ordered by membership created_at DESC");
        rows[0].OwnerUserId.Should().BeNull("NULL owner maps to null");
        rows[1].TenantId.Should().Be(olderTenant);
        rows[1].OwnerUserId.Should().Be(ownerId);
        rows[1].Role.Should().Be("editor");
        rows[1].MembershipStatus.Should().Be("active");
    }

    // ── GetLeaveLookupAsync (ports the leave-lookup single-round-trip) ───────

    [Fact]
    public async Task GetLeaveLookup_ReturnsOwner_AdminCount_AndRole()
    {
        var ownerId = await CreateUserAsync();
        var memberId = await CreateUserAsync();
        var tenantId = await SeedTenantAsync(ownerUserId: ownerId);
        await SeedMembershipAsync(tenantId, ownerId, role: "admin");
        await SeedMembershipAsync(tenantId, memberId, role: "editor");

        var lookup = await _repo.GetLeaveLookupAsync(tenantId, memberId);

        lookup.OwnerUserId.Should().Be(ownerId);
        lookup.ActiveAdminCount.Should().Be(1);
        lookup.Role.Should().Be("editor");
    }

    [Fact]
    public async Task GetLeaveLookup_NullOwnerAndRole_WhenTenantUnknownOrNotMember()
    {
        var lookup = await _repo.GetLeaveLookupAsync(Guid.NewGuid(), Guid.NewGuid());

        lookup.OwnerUserId.Should().BeNull();
        lookup.ActiveAdminCount.Should().Be(0);
        lookup.Role.Should().BeNull("no active membership → role null (policy reads this as NotMember)");
    }

    [Fact]
    public async Task GetLeaveLookup_IgnoresInactiveMemberships()
    {
        var userId = await CreateUserAsync();
        var tenantId = await SeedTenantAsync();
        await SeedMembershipAsync(tenantId, userId, role: "admin", status: "disabled");

        var lookup = await _repo.GetLeaveLookupAsync(tenantId, userId);

        lookup.ActiveAdminCount.Should().Be(0, "only active admin memberships count");
        lookup.Role.Should().BeNull("inactive membership → not a member for leave purposes");
    }

    // ── Mutations ────────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkDeleting_ThenMarkActive_FlipStatus()
    {
        var tenantId = await SeedTenantAsync();

        await _repo.MarkDeletingAsync(tenantId);
        (await QueryScalarAsync<string>("SELECT status FROM tenants WHERE id = @id", tenantId))
            .Should().Be("deleting");

        await _repo.MarkActiveAsync(tenantId);
        (await QueryScalarAsync<string>("SELECT status FROM tenants WHERE id = @id", tenantId))
            .Should().Be("active");
    }

    [Fact]
    public async Task TransferOwnership_SetsNewOwner()
    {
        var oldOwner = await CreateUserAsync();
        var newOwner = await CreateUserAsync();
        var tenantId = await SeedTenantAsync(ownerUserId: oldOwner);

        await _repo.TransferOwnershipAsync(tenantId, newOwner);

        (await _repo.GetOwnerStatusAsync(tenantId))!.OwnerUserId.Should().Be(newOwner);
    }

    [Fact]
    public async Task DeleteMembership_RemovesTheRow()
    {
        var userId = await CreateUserAsync();
        var tenantId = await SeedTenantAsync();
        await SeedMembershipAsync(tenantId, userId);

        await _repo.DeleteMembershipAsync(tenantId, userId);

        (await _repo.GetMembershipRoleStatusAsync(tenantId, userId)).Should().BeNull();
    }
}
