namespace Api.Services;

/// <summary>
/// Path-traversal guard for local file storage. Asserts that a resolved
/// absolute path stays inside a configured base directory, defending against
/// <c>../</c> and absolute-path injection from caller-supplied relative paths.
/// Structurally identical for any local-disk storage backend in either
/// multi-tenant SaaS or single-tenant Community deployments.
/// </summary>
public static class LocalFileStorageGuard
{
    /// <summary>
    /// Throws <see cref="ArgumentException"/> if <paramref name="candidatePath"/>
    /// resolves outside <paramref name="basePath"/>. Both paths are normalized
    /// via <see cref="Path.GetFullPath(string)"/> before comparison; equality
    /// with <paramref name="basePath"/> itself is permitted.
    /// </summary>
    /// <remarks>
    /// Comparison is case-insensitive to match historical SaaS behavior on
    /// Windows-tolerant deployments. Callers are responsible for passing an
    /// already-absolute, fully-resolved <paramref name="basePath"/>.
    /// </remarks>
    public static void AssertWithinBasePath(string basePath, string candidatePath)
    {
        var normalised = Path.GetFullPath(candidatePath);
        if (!normalised.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !normalised.Equals(basePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid file path.");
        }
    }

    /// <summary>
    /// Non-throwing variant for callers that want to log or branch on the
    /// guard outcome without catching exceptions.
    /// </summary>
    public static bool IsWithinBasePath(string basePath, string candidatePath)
    {
        var normalised = Path.GetFullPath(candidatePath);
        return normalised.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalised.Equals(basePath, StringComparison.OrdinalIgnoreCase);
    }
}
