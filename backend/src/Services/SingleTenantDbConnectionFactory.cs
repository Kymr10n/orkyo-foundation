using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Api.Services;

/// <summary>
/// Single-tenant implementation of <see cref="IDbConnectionFactory"/>.
/// All connection types — control-plane, tenant, org, and by-identifier —
/// map to the same connection string: there is no control-plane/tenant split
/// in a single-tenant deployment.
/// </summary>
public sealed class SingleTenantDbConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    /// <param name="connectionString">
    /// The fully-formed connection string for the single deployment database.
    /// The default command-timeout policy is applied during construction.
    /// </param>
    public SingleTenantDbConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = ConnectionStringTimeoutPolicy.ApplyDefaultCommandTimeout(connectionString);
    }

    /// <summary>
    /// Creates a <see cref="SingleTenantDbConnectionFactory"/> from the application configuration.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="connectionStringKey">
    /// The key under <c>ConnectionStrings</c> to read. Defaults to <c>DefaultConnection</c>.
    /// </param>
    public static SingleTenantDbConnectionFactory FromConfiguration(
        IConfiguration configuration,
        string connectionStringKey = "DefaultConnection")
    {
        var cs = configuration.GetConnectionString(connectionStringKey)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{connectionStringKey} is required. " +
                $"Set the ConnectionStrings__{connectionStringKey} environment variable.");
        return new SingleTenantDbConnectionFactory(cs);
    }

    public NpgsqlConnection CreateControlPlaneConnection() => new(_connectionString);
    public NpgsqlConnection CreateTenantConnection(TenantContext tenant) => new(_connectionString);
    public NpgsqlConnection CreateOrgConnection(OrgContext org) => new(_connectionString);
    public NpgsqlConnection CreateConnectionForDatabase(string dbIdentifier) => new(_connectionString);
}
