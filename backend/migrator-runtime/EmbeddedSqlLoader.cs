using System.Reflection;
using Orkyo.Migrations.Abstractions;

namespace Orkyo.Migrator;

/// <summary>
/// Loads embedded SQL resources from a module assembly into <see cref="MigrationScript"/>
/// instances. Module projects expose their SQL as <c>EmbeddedResource</c> entries under
/// <c>sql/controlplane/</c> or <c>sql/tenant/</c>; this loader maps the resource path to
/// a target and id, normalises the SQL, and computes the checksum once at module load.
/// </summary>
/// <remarks>
/// Filename rules:
/// <list type="bullet">
///   <item>Must be under a <c>controlplane/</c> or <c>tenant/</c> subdirectory.</item>
///   <item>Must end in <c>.sql</c>.</item>
///   <item>The stem (filename without extension) becomes the migration <c>Id</c>.</item>
/// </list>
/// Filesystem ordering is never trusted at runtime — the orderer sorts on <c>Id</c>.
/// </remarks>
public static class EmbeddedSqlLoader
{
    public static IReadOnlyCollection<MigrationScript> LoadFromAssembly(
        Assembly assembly,
        string moduleName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

        var prefix = assembly.GetName().Name + ".sql.";
        var scripts = new List<MigrationScript>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) continue;

            // Resource path looks like:  Orkyo.Saas.Migrations.sql.controlplane.V001__schema.sql
            // Trim the prefix to get:    controlplane.V001__schema.sql
            var relative = resourceName[prefix.Length..];
            var target = ParseTarget(relative, resourceName);

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{resourceName}' could not be opened.");
            using var reader = new StreamReader(stream);
            var rawSql = reader.ReadToEnd();
            var normalized = NormalizeSql(rawSql);
            var checksum = ChecksumPolicy.Compute(normalized);

            var id = ExtractId(relative);

            scripts.Add(new MigrationScript(
                Id: id,
                Module: moduleName,
                TargetDatabase: target,
                Sql: normalized,
                Checksum: checksum,
                DependsOn: Array.Empty<string>()));
        }

        return scripts;
    }

    private static MigrationTargetDatabase ParseTarget(string relative, string resourceName)
    {
        if (relative.StartsWith("controlplane.", StringComparison.Ordinal))
            return MigrationTargetDatabase.ControlPlane;
        if (relative.StartsWith("tenant.", StringComparison.Ordinal))
            return MigrationTargetDatabase.Tenant;

        throw new InvalidOperationException(
            $"Embedded migration '{resourceName}' is not under sql/controlplane/ or sql/tenant/. " +
            $"Every migration must be classified by target.");
    }

    private static string ExtractId(string relative)
    {
        // relative: "controlplane.V001__schema.sql"  →  id: "V001__schema"
        // strip leading "controlplane." or "tenant."
        var afterTarget = relative.IndexOf('.') + 1;
        var withoutTarget = relative[afterTarget..];
        // strip trailing ".sql"
        return withoutTarget[..^".sql".Length];
    }

    private static string NormalizeSql(string sql) =>
        sql.Replace("\r\n", "\n").Replace("\r", "\n");
}
