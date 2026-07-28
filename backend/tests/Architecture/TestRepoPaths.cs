namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// Locates repository directories from the test assembly's output folder, for the
/// architecture guards that read source files rather than compiled types.
/// </summary>
internal static class TestRepoPaths
{
    /// <summary>
    /// Walks up from the test assembly's base directory looking for the given
    /// path segments, e.g. <c>FindDirectory("backend", "src", "Endpoints")</c>.
    /// Returns null when no ancestor contains it — callers assert on that so a
    /// moved layout fails loudly instead of silently skipping the guard.
    /// </summary>
    public static string? FindDirectory(params string[] pathSegments)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12; i++)
        {
            var candidate = Path.Combine([dir, .. pathSegments]);
            if (Directory.Exists(candidate)) return candidate;

            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == null) break;
            dir = parent;
        }
        return null;
    }
}
