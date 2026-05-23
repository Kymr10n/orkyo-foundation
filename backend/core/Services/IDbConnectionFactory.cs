using Api.Models;
using Npgsql;

namespace Api.Services;

/// <summary>
/// Factory for creating database connections with proper connection-string management.
/// The interface is intentionally composition-agnostic: multi-tenant SaaS and single-tenant
/// Community both consume it. Community is expected to provide an implementation that maps
/// both <see cref="CreateControlPlaneConnection"/> and <see cref="CreateTenantConnection"/>
/// to the single deployment database (the control-plane / tenant split collapses there).
/// </summary>
public interface IDbConnectionFactory : IOrgDbConnectionFactory
{
    /// <summary>Creates a connection to the control-plane database (auth / tenants).</summary>
    NpgsqlConnection CreateControlPlaneConnection();

    /// <summary>Creates a connection to a tenant's database (business data).</summary>
    NpgsqlConnection CreateTenantConnection(TenantContext tenant);

    /// <summary>
    /// Creates a connection to a tenant database by its database identifier
    /// (e.g. <c>tenant_acme</c>). Useful during provisioning when no
    /// <see cref="TenantContext"/> is available yet.
    /// </summary>
    NpgsqlConnection CreateConnectionForDatabase(string dbIdentifier);
}
