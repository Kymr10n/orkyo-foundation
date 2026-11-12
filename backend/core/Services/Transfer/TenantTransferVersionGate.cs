using Api.Constants;
using Api.Models;
using Orkyo.Migrations.Abstractions;

namespace Api.Services.Transfer;

/// <summary>
/// Decides whether an archive's rows have the shape and meaning this installation's tenant
/// schema expects. An import loads rows and runs no migrations, so the only safe answer is
/// "the same schema version": the same set of tenant-phase foundation migrations.
/// <para>
/// An older archive is refused even when the gap is additive. A migration's class header
/// describes rollback safety, not whether its effect can be skipped: several <c>expand</c>
/// migrations also backfill rows, and an archive that predates one would load without that
/// data.
/// </para>
/// </summary>
public sealed class TenantTransferVersionGate
{
    // The module name foundation's scripts carry, in both editions. Product modules are left
    // out: Community's tenant-phase scripts add no table and no column, and comparing them
    // would make every Community archive differ from every SaaS tenant.
    private const string FoundationModule = "foundation";

    private readonly Lazy<IReadOnlyList<string>> _current;

    public TenantTransferVersionGate(IEnumerable<IMigrationModule> modules)
    {
        _current = new Lazy<IReadOnlyList<string>>(() =>
        {
            // A script that only a per-tenant database runs (MigrationScope.TenantDatabaseOnly)
            // is absent from Community's journal, so it cannot be part of a version the two
            // editions must agree on.
            var ids = modules
                .SelectMany(m => m.GetMigrations())
                .Where(s => s.Module == FoundationModule
                    && s.TargetDatabase == MigrationTargetDatabase.Tenant
                    && s.Scope == MigrationScope.Default)
                .Select(s => s.Id)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            return ids.Count > 0
                ? ids
                : throw new InvalidOperationException(
                    "Tenant transfer needs the migration modules: no tenant-phase foundation migration " +
                    "is registered. The host must call AddFoundationMigrations() or its edition's equivalent.");
        });
    }

    /// <summary>The schema version this build expects of a tenant database, sorted.</summary>
    public IReadOnlyList<string> Current => _current.Value;

    /// <summary>
    /// A blocker when the tenant database has not applied every migration this build expects.
    /// Applies to both directions: an export of a database that is behind would label its rows
    /// with a version they do not have.
    /// </summary>
    public TransferBlocker? CheckDatabase(IReadOnlyCollection<string> journalIds)
    {
        var applied = journalIds.ToHashSet(StringComparer.Ordinal);
        return Current.All(applied.Contains)
            ? null
            : new TransferBlocker(ApiErrorCodes.TransferSchemaBehind,
                "The tenant database is behind this application version. Run the migrator first.");
    }

    /// <summary>A blocker when the archive's schema version is not this build's.</summary>
    public TransferBlocker? CheckArchive(IReadOnlyCollection<string> archiveSchemaVersion)
    {
        var archive = archiveSchemaVersion.ToHashSet(StringComparer.Ordinal);
        var current = Current.ToHashSet(StringComparer.Ordinal);
        if (archive.SetEquals(current)) return null;

        var message = archive.IsProperSubsetOf(current)
            ? "The archive comes from an older schema version. Upgrade the source installation and export again."
            : archive.IsProperSupersetOf(current)
                ? "The archive comes from a newer schema version. Upgrade this installation first."
                : "The archive comes from a different schema version. Bring both installations to the same " +
                  "version and export again.";
        return new TransferBlocker(ApiErrorCodes.TransferVersionMismatch, message);
    }
}
