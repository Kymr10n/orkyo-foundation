namespace Orkyo.Migrations.Abstractions;

/// <summary>
/// Which deployment shapes a migration may run in. Most migrations run everywhere
/// (<see cref="Default"/>). A tenant-phase migration that assumes the tenant schema lives in
/// its own database — one that would collide with, or destroy, a control-plane table when both
/// share one database — declares <see cref="TenantDatabaseOnly"/>, and an edition that collapses
/// control plane and tenant into one database skips it.
/// </summary>
/// <remarks>
/// Declared in the file as a whole-line comment, <c>-- @scope: tenant-database-only</c>, so the
/// decision sits next to the SQL and shows up in its diff. Migrations that predate the directive
/// cannot be edited (applied migrations are immutable), so a module may also mark ids by name.
/// </remarks>
public enum MigrationScope
{
    Default = 0,
    TenantDatabaseOnly = 1,
}
