using Xunit;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// The 2026-08 review found eleven barrel <c>index.ts</c> files with zero importers —
/// every consumer imports by deep path, and the barrels only accumulated stale re-exports.
/// They were deleted; this pins the survivors so new barrels are a decision, not a habit.
///
/// A C# test scanning the frontend is the established pattern here
/// (ApiPathContractTests reads api-paths.ts): the frontend has no source-scanning tests
/// of its own, and this runs in the same dotnet test CI step as every other ratchet.
/// </summary>
public class FrontendBarrelTests
{
    /// <summary>
    /// The barrels that earn their keep, by importer count at the 2026-08 review
    /// (lib/utils ×65, store ×48, types ×42, constants ×19, components/layout ×8,
    /// components/ui ×2). Adding a barrel means adding it here with the reason it
    /// exists — a barrel nobody imports through is clutter with a maintenance cost.
    /// </summary>
    private static readonly HashSet<string> KnownBarrels = new(StringComparer.Ordinal)
    {
        "components/layout/index.ts",
        "components/ui/index.ts",
        "constants/index.ts",
        "lib/utils/index.ts",
        "store/index.ts",
        "types/index.ts",
    };

    [Fact]
    public void NoNewBarrelFiles_AppearUnderFrontendSrc()
    {
        var srcDir = TestRepoPaths.FindDirectory("frontend", "src");
        srcDir.Should().NotBeNull("could not locate frontend/src");

        var barrels = Directory.GetFiles(srcDir!, "index.ts", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(srcDir!, f).Replace('\\', '/'))
            .OrderBy(rel => rel, StringComparer.Ordinal)
            .ToList();

        barrels.Should().NotBeEmpty("the scan found no index.ts at all — did the layout move?");

        var unexpected = barrels.Where(b => !KnownBarrels.Contains(b)).ToList();

        unexpected.Should().BeEmpty(
            "eleven zero-importer barrels were deleted in the 2026-08 review; consumers "
            + "import by deep path. A new barrel needs a KnownBarrels entry with the "
            + "reason it exists. Unexpected:\n  " + string.Join("\n  ", unexpected));
    }

    [Fact]
    public void KnownBarrels_HaveNoStaleEntries()
    {
        var srcDir = TestRepoPaths.FindDirectory("frontend", "src");
        srcDir.Should().NotBeNull("could not locate frontend/src");

        var existing = Directory.GetFiles(srcDir!, "index.ts", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(srcDir!, f).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        var stale = KnownBarrels.Where(b => !existing.Contains(b)).ToList();

        stale.Should().BeEmpty(
            "these KnownBarrels entries no longer exist — remove them so the list stays "
            + "honest:\n  " + string.Join("\n  ", stale));
    }
}
