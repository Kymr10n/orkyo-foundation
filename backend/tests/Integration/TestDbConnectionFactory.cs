using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// Minimal test-only <see cref="IDbConnectionFactory"/> that opens new connections
/// against caller-supplied connection strings. Lets foundation tests exercise
/// foundation-owned services (<see cref="TenantUserService"/>,
/// <see cref="SiteSettingsRepository"/>, <see cref="UserManagementService"/>,
/// <see cref="KeycloakIdentityLinkService"/>, etc.) end-to-end against a real
/// Postgres container without depending on SaaS's concrete
/// <c>DbConnectionFactory</c> (which has SaaS-specific multi-tenant topology logic).
///
/// Mirrors the single-database shape Community uses: tenant connections come from
/// the <see cref="TenantContext"/>, control-plane and admin connections from the
/// caller-supplied strings.
/// </summary>
public sealed class TestDbConnectionFactory : IDbConnectionFactory
{
    private readonly string _controlPlaneConnectionString;
    private readonly string _adminConnectionString;

    public TestDbConnectionFactory(string controlPlaneConnectionString, string adminConnectionString)
    {
        _controlPlaneConnectionString = controlPlaneConnectionString;
        _adminConnectionString = adminConnectionString;
    }

    public NpgsqlConnection CreateControlPlaneConnection() => new(_controlPlaneConnectionString);

    public NpgsqlConnection CreateTenantConnection(TenantContext tenant) =>
        new(tenant.TenantDbConnectionString);

    public NpgsqlConnection CreateOrgConnection(OrgContext org) =>
        new(org.DbConnectionString);

    public NpgsqlConnection CreateConnectionForDatabase(string dbIdentifier)
    {
        var builder = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = dbIdentifier,
        };
        return new NpgsqlConnection(builder.ConnectionString);
    }
}
