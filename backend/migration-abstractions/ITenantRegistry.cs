namespace Orkyo.Migrations.Abstractions;

/// <summary>
/// Discovers the per-tenant databases that the runner should iterate over after
/// control-plane migrations succeed. Implementations live in the product layer
/// (e.g. <c>Orkyo.Saas.Migrations</c>) because tenant-registry shape is product-specific.
/// </summary>
/// <remarks>
/// Foundation owns the contract; product migrators register an implementation so the
/// foundation CLI can iterate tenants without knowing SaaS-specific schema.
/// In Community deployments, an implementation that returns a single
/// <see cref="TenantDatabase"/> pointing at the deployment DB is sufficient.
/// </remarks>
public interface ITenantRegistry
{
    /// <summary>
    /// Returns the tenants that should receive tenant-target migrations. Caller has
    /// already established that the control-plane DB is up-to-date enough to read
    /// the registry.
    /// </summary>
    /// <param name="controlPlaneConnectionString">
    /// Connection string for the database that hosts the tenant registry. Treated as
    /// opaque by foundation — only the implementation knows what shape to query.
    /// </param>
    Task<IReadOnlyList<TenantDatabase>> ListActiveTenantsAsync(
        string controlPlaneConnectionString,
        CancellationToken cancellationToken);
}

/// <summary>
/// Description of a single tenant database the runner should target.
/// </summary>
/// <param name="Id">Stable tenant identifier (typically a UUID). Used in lock keys and logs.</param>
/// <param name="Slug">Human-meaningful slug for CLI filtering (e.g. <c>--tenant-slug demo</c>).</param>
/// <param name="ConnectionString">
/// Open-able connection string for the tenant database. The product implementation is
/// responsible for assembling this from per-tenant credentials / hostnames.
/// </param>
public sealed record TenantDatabase(
    string Id,
    string Slug,
    string ConnectionString);
