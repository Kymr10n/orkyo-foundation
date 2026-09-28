using Npgsql;
using Orkyo.Migrations.Abstractions;

namespace Orkyo.Foundation.Tests;

/// <summary>
/// Shared database fixture for the endpoint suite: two databases (control plane + test
/// tenant) on the server <see cref="TestPostgresBootstrap"/> provides, migrated and seeded
/// once, with a <see cref="FoundationWebApplicationFactory"/> over them.
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    /// <summary>Connection string for the control-plane database.</summary>
    public string ControlPlaneConnectionString { get; private set; } = null!;

    /// <summary>Connection string for the shared test tenant database.</summary>
    public string TenantConnectionString { get; private set; } = null!;

    /// <summary>The one site the fixture seeds into the test tenant.</summary>
    public static readonly Guid SiteId = new("5e5e0000-0000-0000-0000-000000000001");

    /// <summary>The one space the fixture seeds, homed at <see cref="SiteId"/>.</summary>
    public static readonly Guid SpaceId = new("5e5e0000-0000-0000-0000-000000000002");

    /// <summary>Gets the shared web application factory for all tests.</summary>
    public FoundationWebApplicationFactory Factory { get; private set; } = null!;

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with the standard test tenant slug and
    /// bearer-token authorization headers preset. Use this from integration tests
    /// instead of repeating the header wiring in every constructor.
    /// </summary>
    public HttpClient CreateAuthorizedClient(string tenantSlug = TestConstants.TenantSlug)
        => CreateClientWithToken(TestConstants.TestBearerToken, tenantSlug);

    /// <summary>
    /// Creates an <see cref="HttpClient"/> authorized as the shared test user with a
    /// specific tenant <paramref name="role"/> ("admin" | "editor" | "viewer"), for
    /// exercising role-gated authorization on endpoints.
    /// </summary>
    public HttpClient CreateClientWithRole(string role, string tenantSlug = TestConstants.TenantSlug)
        => CreateClientWithToken(TestConstants.BearerTokenForRole(role), tenantSlug);

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with the tenant slug header and an arbitrary
    /// <paramref name="bearerToken"/> preset.
    /// </summary>
    public HttpClient CreateClientWithToken(string bearerToken, string tenantSlug = TestConstants.TenantSlug)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, tenantSlug);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearerToken}");
        return client;
    }

    /// <summary>
    /// Creates a new member of the test tenant with <paramref name="role"/> and returns a bearer
    /// token for them: a distinct user, for tests whose rows must not land on the shared one.
    /// </summary>
    public static async Task<string> CreateMemberTokenAsync(string role)
    {
        var email = $"member_{Guid.NewGuid():N}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(email, "Test Member", TestConstants.TenantSlug, role, active: true);
        return TestConstants.BearerToken(userId.ToString(), email, "Test Member", TestConstants.TenantId.ToString(),
            TestConstants.TenantSlug, isTenantAdmin: role == RoleConstants.Admin, role: role);
    }

    /// <summary>
    /// Creates a linked user with no tenant (see <see cref="DatabaseTestUtils.CreateLinkedUserAsync"/>)
    /// and returns their bearer token; a site admin when <paramref name="siteAdmin"/>.
    /// </summary>
    public static async Task<string> CreateLinkedTokenAsync(string prefix, bool siteAdmin = false)
        => (await DatabaseTestUtils.CreateLinkedUserAsync(prefix, siteAdmin)).Token;

    public async Task InitializeAsync()
    {
        var server = await TestPostgresBootstrap.GetAsync();
        ControlPlaneConnectionString = server.ConnectionStringFor("control_plane");
        TenantConnectionString = server.ConnectionStringFor(TestConstants.TenantDatabase);

        // Store the port for helpers that need direct DB connections
        DatabaseTestUtils.SetDatabasePort(server.Port);

        await CreateAndMigrateDatabasesAsync(server);

        Factory = await FoundationWebApplicationFactory.CreateAsync(
            TenantConnectionString, ControlPlaneConnectionString);
        Console.WriteLine("✅ Test database ready — all tests will share this clean state");
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();
    }

    // ── DB setup ──────────────────────────────────────────────────────────────

    private static async Task CreateAndMigrateDatabasesAsync(TestPostgresServer server)
    {
        Console.WriteLine("  🗑️  Creating databases...");
        await TestPostgresBootstrap.EnsureDatabaseAsync(server, "control_plane");
        await TestPostgresBootstrap.EnsureDatabaseAsync(server, TestConstants.TenantDatabase);

        var cpCs = server.ConnectionStringFor("control_plane");
        var tenantCs = server.ConnectionStringFor(TestConstants.TenantDatabase);
        await ApplyMigrationsAsync(cpCs, tenantCs);

        // Seed control plane test data
        await using var seedConn = new NpgsqlConnection(cpCs);
        await seedConn.OpenAsync();

        await using var tenantSeedCmd = new NpgsqlCommand(
            @"INSERT INTO tenants (id, slug, display_name, status, db_identifier, tier, created_at, updated_at)
              VALUES (@id, @slug, 'Test Organization', 'active', @db, 2, NOW(), NOW())
              ON CONFLICT (id) DO UPDATE SET slug = @slug, tier = 2, db_identifier = @db", seedConn);
        tenantSeedCmd.Parameters.AddWithValue("id", TestConstants.TenantId);
        tenantSeedCmd.Parameters.AddWithValue("slug", TestConstants.TenantSlug);
        tenantSeedCmd.Parameters.AddWithValue("db", TestConstants.TenantDatabase);
        await tenantSeedCmd.ExecuteNonQueryAsync();
        Console.WriteLine($"    ✓ Test tenant '{TestConstants.TenantSlug}' seeded at Enterprise tier");

        await using var userCmd = new NpgsqlCommand(
            @"INSERT INTO users (id, email, display_name, status, created_at, updated_at)
              VALUES (@id, @email, @name, 'active', NOW(), NOW())
              ON CONFLICT (id) DO NOTHING", seedConn);
        userCmd.Parameters.AddWithValue("id", TestConstants.UserId);
        userCmd.Parameters.AddWithValue("email", "test@orkyo.example");
        userCmd.Parameters.AddWithValue("name", "Test User");
        await userCmd.ExecuteNonQueryAsync();
        Console.WriteLine("    ✓ Test user seeded");

        await using var memberCmd = new NpgsqlCommand(
            @"INSERT INTO tenant_memberships (user_id, tenant_id, role, status, created_at, updated_at)
              SELECT @userId, t.id, 'admin', 'active', NOW(), NOW()
              FROM tenants t WHERE t.slug = @slug
              ON CONFLICT DO NOTHING", seedConn);
        memberCmd.Parameters.AddWithValue("userId", TestConstants.UserId);
        memberCmd.Parameters.AddWithValue("slug", TestConstants.TenantSlug);
        await memberCmd.ExecuteNonQueryAsync();
        Console.WriteLine($"    ✓ Test user seeded as admin of tenant '{TestConstants.TenantSlug}'");

        // Seed one criterion of each data type into the tenant database
        await using var tenantSeedConn = new NpgsqlConnection(tenantCs);
        await tenantSeedConn.OpenAsync();

        // The three classic types the suite assumes. Shared with PostgresFixture — see
        // TestResourceTypes for why migrations no longer provide them.
        await TestResourceTypes.EnsureAsync(tenantSeedConn);
        Console.WriteLine("    ✓ Fixture resource types (space, person, tool) ensured");

        await using var criteriaCmd = new NpgsqlCommand(@"
            INSERT INTO criteria (name, description, data_type, enum_values, created_at, updated_at)
            VALUES
                ('seed_boolean', 'Seed Boolean criterion',  'Boolean', NULL,                                NOW(), NOW()),
                ('seed_number',  'Seed Number criterion',   'Number',  NULL,                                NOW(), NOW()),
                ('seed_string',  'Seed String criterion',   'String',  NULL,                                NOW(), NOW()),
                ('seed_enum',    'Seed Enum criterion',     'Enum',    '[""Option A"",""Option B""]'::jsonb, NOW(), NOW())
            ON CONFLICT (name) DO NOTHING", tenantSeedConn);
        await criteriaCmd.ExecuteNonQueryAsync();
        Console.WriteLine("    ✓ Seed criteria (Boolean, Number, String, Enum) inserted into tenant database");

        // Assign seed criteria to all resource types so tests can use them with any
        // resource type (space, person, tool) without triggering the cross-type check.
        await using var applicabilityCmd = new NpgsqlCommand(@"
            INSERT INTO criterion_resource_types (criterion_id, resource_type_id)
            SELECT c.id, rt.id
            FROM criteria c
            CROSS JOIN resource_types rt
            WHERE c.name IN ('seed_boolean', 'seed_number', 'seed_string', 'seed_enum')
            ON CONFLICT DO NOTHING", tenantSeedConn);
        await applicabilityCmd.ExecuteNonQueryAsync();
        Console.WriteLine("    ✓ Seed criteria applicability assigned for all resource types");

        // One site and one space, so a test that needs "a site" or "a space" names a fixed id
        // instead of depending on what an earlier test happened to create.
        await using var placeCmd = new NpgsqlCommand(@"
            INSERT INTO sites (id, name, code) VALUES (@site, 'Test Site', 'fixture-site')
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO resources (id, resource_type_id, name, code, allocation_mode,
                                   home_site_id, cross_site_allowed, is_physical)
            SELECT @space, rt.id, 'Test Space', 'FIXTURE-SPACE', 'Exclusive', @site, false, false
            FROM resource_types rt WHERE rt.key = 'space'
            ON CONFLICT (id) DO NOTHING", tenantSeedConn);
        placeCmd.Parameters.AddWithValue("site", SiteId);
        placeCmd.Parameters.AddWithValue("space", SpaceId);
        await placeCmd.ExecuteNonQueryAsync();
        Console.WriteLine("    ✓ Fixture site and space seeded");

        // Mirror the shared test user into the tenant users table so FK constraints
        // on user_preferences, preset_applications, etc. are satisfied.
        await using var tenantUserCmd = new NpgsqlCommand(
            @"INSERT INTO users (id, email, display_name, created_at, synced_at)
              VALUES (@id, @email, @name, NOW(), NOW())
              ON CONFLICT (id) DO NOTHING", tenantSeedConn);
        tenantUserCmd.Parameters.AddWithValue("id", TestConstants.UserId);
        tenantUserCmd.Parameters.AddWithValue("email", "test@orkyo.example");
        tenantUserCmd.Parameters.AddWithValue("name", "Test User");
        await tenantUserCmd.ExecuteNonQueryAsync();
        Console.WriteLine("    ✓ Test user mirrored into tenant database");
    }

    private static async Task ApplyMigrationsAsync(string cpCs, string tenantCs)
    {
        Console.WriteLine("  📊 Applying migrations...");
        try
        {
            var runner = TestPostgresBootstrap.BuildFoundationRunner();

            await runner.RunAsync(cpCs, MigrationTargetDatabase.ControlPlane, "foundation-test-cp");
            Console.WriteLine("    ✓ ControlPlane migrations applied");

            await runner.RunAsync(tenantCs, MigrationTargetDatabase.Tenant, "foundation-test-tenant");
            Console.WriteLine("    ✓ Tenant migrations applied");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Migration failed: {ex.Message}");
            throw;
        }
    }
}

/// <summary>
/// Defines the test collection that shares the database fixture.
/// All test classes annotated with <c>[Collection("Database collection")]</c>
/// will share the same PostgreSQL container.
/// </summary>
[CollectionDefinition("Database collection")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    // This class is never instantiated. It exists only to define the collection.
}
