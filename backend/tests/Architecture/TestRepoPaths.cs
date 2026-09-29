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

    private static readonly string[] BackendRoots = ["src", "core", "seeding", "shared"];

    // Read once per test run: every source guard scans the same files.
    private static readonly Lazy<IReadOnlyList<SourceFile>> Sources = new(LoadBackendSources);

    /// <summary>
    /// The <c>.cs</c> files under the given <c>backend/</c> roots ("src", "core", "seeding", "shared"),
    /// without build output. Fails when a root has no files, so a moved layout cannot turn a
    /// guard into a scan of nothing.
    /// </summary>
    public static IReadOnlyList<SourceFile> BackendSources(params string[] roots)
    {
        var files = Sources.Value.Where(f => roots.Contains(f.Root)).ToList();
        foreach (var root in roots)
            files.Should().Contain(f => f.Root == root,
                $"the source scan found no .cs files under backend/{root} — did the layout move?");
        return files;
    }

    private static IReadOnlyList<SourceFile> LoadBackendSources()
    {
        var bin = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var results = new List<SourceFile>();
        foreach (var root in BackendRoots)
        {
            var dir = FindDirectory("backend", root);
            dir.Should().NotBeNull($"could not locate backend/{root}");

            results.AddRange(Directory.GetFiles(dir!, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(bin) && !f.Contains(obj))
                .Select(f => new SourceFile(root, Path.GetRelativePath(dir!, f).Replace('\\', '/'), File.ReadAllText(f))));
        }
        return results;
    }
}

/// <summary>A backend source file: <see cref="Rel"/> is its path below <c>backend/{Root}</c>.</summary>
internal sealed record SourceFile(string Root, string Rel, string Text)
{
    /// <summary>"root:rel", unambiguous when the same relative path exists under two roots.</summary>
    public string Key => $"{Root}:{Rel}";
}
