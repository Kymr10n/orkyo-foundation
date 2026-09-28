namespace Orkyo.Migrations.Abstractions;

/// <summary>
/// Which deployment shapes a migration may run in. Most migrations run everywhere
/// (<see cref="Default"/>). A tenant-phase migration that assumes the tenant schema lives in
/// its own database — one that would collide with, or destroy, a control-plane table when both
/// share one database — declares <see cref="TenantDatabaseOnly"/>, and an edition that collapses
/// control plane and tenant into one database skips it.
/// </summary>
/// <remarks>
/// A module marks the ids it scopes (applied migrations are immutable, so the mark cannot be added
/// to a file that has already run).
/// </remarks>
public enum MigrationScope
{
    Default = 0,
    TenantDatabaseOnly = 1,
}
