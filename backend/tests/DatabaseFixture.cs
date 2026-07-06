using Npgsql;
using Orkyo.Foundation.Migrations;
using Orkyo.Migrations.Abstractions;
using Orkyo.Migrator;
using Testcontainers.PostgreSql;
using Xunit;

namespace Orkyo.Foundation.Tests;

/// <summary>
/// Shared database fixture that provides PostgreSQL for integration tests.
/// In CI (when CI=true and a service container is available on port 5432),
/// connects directly to the pre-existing database — skipping Testcontainers
/// startup. Locally, spins up a Testcontainers PostgreSQL instance.
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;

    /// <summary>Gets the port on which the test database is listening.</summary>
    public int DatabasePort { get; private set; }

    /// <summary>Gets the shared web application factory for all tests.</summary>
    public FoundationWebApplicationFactory Factory { get; private set; } = null!;

    private bool UseCiDatabase =>
        Environment.GetEnvironmentVariable("CI") == "true"
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres"));

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with the standard test tenant slug and
    /// bearer-token authorization headers preset. Use this from integration tests
    /// instead of repeating the header wiring in every constructor.
    /// </summary>
    public HttpClient CreateAuthorizedClient(string tenantSlug = TestConstants.TenantSlug)
        => CreateClient(TestConstants.TestBearerToken, tenantSlug);

    /// <summary>
    /// Creates an <see cref="HttpClient"/> authorized as the shared test user with a
    /// specific tenant <paramref name="role"/> ("admin" | "editor" | "viewer"), for
    /// exercising role-gated authorization on endpoints.
    /// </summary>
    public HttpClient CreateClientWithRole(string role, string tenantSlug = TestConstants.TenantSlug)
        => CreateClient(TestConstants.BearerTokenForRole(role), tenantSlug);

    private HttpClient CreateClient(string bearerToken, string tenantSlug)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, tenantSlug);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearerToken}");
        return client;
    }

    public async Task InitializeAsync()
    {
        if (UseCiDatabase)
        {
            DatabasePort = 5432;
            Console.WriteLine("⚡ CI detected — using service container on port 5432 (skipping Testcontainers)");
        }
        else
        {
            Console.WriteLine("🚀 Starting PostgreSQL test container...");

            _postgresContainer = new PostgreSqlBuilder("postgres:16-alpine")
                .WithImage("postgres:16-alpine")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .WithCleanUp(true)
                .Build();

            await _postgresContainer.StartAsync();

            DatabasePort = _postgresContainer.GetMappedPublicPort(5432);
            Console.WriteLine($"  ✓ PostgreSQL container started on port {DatabasePort}");
        }

        // Store the port for helpers that need direct DB connections
        DatabaseTestUtils.SetDatabasePort(DatabasePort);

        await CreateAndMigrateDatabasesAsync();

        var tenantCs = $"Host=localhost;Port={DatabasePort};Database={TestConstants.TenantDatabase};Username=postgres;Password=postgres";
        var controlPlaneCs = $"Host=localhost;Port={DatabasePort};Database=control_plane;Username=postgres;Password=postgres";

        Factory = await FoundationWebApplicationFactory.CreateAsync(tenantCs, controlPlaneCs);
        Console.WriteLine("✅ Test database ready — all tests will share this clean state");
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();

        if (_postgresContainer is not null)
        {
            Console.WriteLine("🛑 Stopping PostgreSQL test container...");
            await _postgresContainer.DisposeAsync();
            Console.WriteLine("✅ Test container stopped and cleaned up");
        }
    }

    // ── DB setup ──────────────────────────────────────────────────────────────

    private async Task CreateAndMigrateDatabasesAsync()
    {
        Console.WriteLine("  🗑️  Creating databases...");

        var postgresConn = $"Host=localhost;Port={DatabasePort};Database=postgres;Username=postgres;Password=postgres";
        await using var conn = new NpgsqlConnection(postgresConn);
        await conn.OpenAsync();

        foreach (var db in new[] { "control_plane", TestConstants.TenantDatabase })
        {
            await using var checkCmd = new NpgsqlCommand(
                $"SELECT 1 FROM pg_database WHERE datname = '{db}'", conn);
            var exists = await checkCmd.ExecuteScalarAsync() != null;

            if (!exists)
            {
                await using var cmd = new NpgsqlCommand($"CREATE DATABASE {db}", conn);
                await cmd.ExecuteNonQueryAsync();
                Console.WriteLine($"    ✓ Created {db}");
            }
            else
            {
                Console.WriteLine($"    ✓ Database {db} already exists");
            }
        }

        await ApplyMigrationsAsync();

        // Seed control plane test data
        var cpCs = $"Host=localhost;Port={DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
        await using var seedConn = new NpgsqlConnection(cpCs);
        await seedConn.OpenAsync();

        await using var tenantSeedCmd = new NpgsqlCommand(
            @"INSERT INTO tenants (id, slug, display_name, status, db_identifier, tier, created_at, updated_at)
              VALUES (@id, @slug, 'Test Organization', 'active', @db, 2, NOW(), NOW())
              ON CONFLICT (id) DO UPDATE SET slug = @slug, tier = 2, db_identifier = @db", seedConn);
        tenantSeedCmd.Parameters.AddWithValue("id", new Guid("00000000-0000-0000-0000-000000000001"));
        tenantSeedCmd.Parameters.AddWithValue("slug", TestConstants.TenantSlug);
        tenantSeedCmd.Parameters.AddWithValue("db", TestConstants.TenantDatabase);
        await tenantSeedCmd.ExecuteNonQueryAsync();
        Console.WriteLine($"    ✓ Test tenant '{TestConstants.TenantSlug}' seeded at Enterprise tier");

        await using var userCmd = new NpgsqlCommand(
            @"INSERT INTO users (id, email, display_name, status, created_at, updated_at)
              VALUES (@id, @email, @name, 'active', NOW(), NOW())
              ON CONFLICT (id) DO NOTHING", seedConn);
        userCmd.Parameters.AddWithValue("id", new Guid("11111111-1111-1111-1111-111111111111"));
        userCmd.Parameters.AddWithValue("email", "test@orkyo.example");
        userCmd.Parameters.AddWithValue("name", "Test User");
        await userCmd.ExecuteNonQueryAsync();
        Console.WriteLine("    ✓ Test user seeded");

        await using var memberCmd = new NpgsqlCommand(
            @"INSERT INTO tenant_memberships (user_id, tenant_id, role, status, created_at, updated_at)
              SELECT @userId, t.id, 'admin', 'active', NOW(), NOW()
              FROM tenants t WHERE t.slug = @slug
              ON CONFLICT DO NOTHING", seedConn);
        memberCmd.Parameters.AddWithValue("userId", new Guid("11111111-1111-1111-1111-111111111111"));
        memberCmd.Parameters.AddWithValue("slug", TestConstants.TenantSlug);
        await memberCmd.ExecuteNonQueryAsync();
        Console.WriteLine($"    ✓ Test user seeded as admin of tenant '{TestConstants.TenantSlug}'");

        // Seed one criterion of each data type into the tenant database
        var tenantCs = $"Host=localhost;Port={DatabasePort};Database={TestConstants.TenantDatabase};Username=postgres;Password=postgres";
        await using var tenantSeedConn = new NpgsqlConnection(tenantCs);
        await tenantSeedConn.OpenAsync();
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

        // Mirror the shared test user into the tenant users table so FK constraints
        // on user_preferences, preset_applications, etc. are satisfied.
        await using var tenantUserCmd = new NpgsqlCommand(
            @"INSERT INTO users (id, email, display_name, created_at, synced_at)
              VALUES (@id, @email, @name, NOW(), NOW())
              ON CONFLICT (id) DO NOTHING", tenantSeedConn);
        tenantUserCmd.Parameters.AddWithValue("id", new Guid("11111111-1111-1111-1111-111111111111"));
        tenantUserCmd.Parameters.AddWithValue("email", "test@orkyo.example");
        tenantUserCmd.Parameters.AddWithValue("name", "Test User");
        await tenantUserCmd.ExecuteNonQueryAsync();
        Console.WriteLine("    ✓ Test user mirrored into tenant database");
    }

    private async Task ApplyMigrationsAsync()
    {
        Console.WriteLine("  📊 Applying migrations...");
        try
        {
            var runner = new MigrationRunner([new FoundationMigrationModule()]);

            var cpCs = $"Host=localhost;Port={DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
            await runner.RunAsync(cpCs, MigrationTargetDatabase.ControlPlane, "foundation-test-cp");
            Console.WriteLine("    ✓ ControlPlane migrations applied");

            var tenantCs = $"Host=localhost;Port={DatabasePort};Database={TestConstants.TenantDatabase};Username=postgres;Password=postgres";
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
