using System.Text.RegularExpressions;
using Xunit;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// Guards the single cache. Five hand-rolled process-wide caches had accumulated — three
/// <c>MemoryCache</c> statics in <c>ContextEnrichmentMiddleware</c>, a
/// <c>ConcurrentDictionary</c> plus a locked tuple in <c>TenantSettingsService</c>, another in
/// <c>SiteSettingsService</c>, and <c>ShortTtlCache</c> — each with its own TTL handling, its own
/// eviction story, and a <c>ClearCache()</c> that only the tests called. They are now one
/// injected <c>SingleFlightCache</c> over the host's <c>IMemoryCache</c>, which a test host can
/// empty in one call and a product can size once.
///
/// <para>This is a source-level ratchet, the same idiom as
/// <see cref="ErrorShapeContractTests"/>: it fails on the three ways a sixth private cache gets
/// introduced. The allowlist is empty on purpose.</para>
/// </summary>
public partial class StaticCacheContractTests
{
    /// <summary>
    /// Files allowed to hold their own process-wide cache, each for a stated reason. Empty:
    /// a new entry needs a reason that the shared cache genuinely cannot serve.
    /// </summary>
    private static readonly HashSet<string> ExemptFiles = new(StringComparer.Ordinal);

    [GeneratedRegex(@"static\s+(?:readonly\s+)?MemoryCache\b")]
    private static partial Regex StaticMemoryCacheRegex();

    [GeneratedRegex(@"static\s+(?:readonly\s+)?ConcurrentDictionary<[^>]*(?:Cache|Entry)")]
    private static partial Regex StaticCacheDictionaryRegex();

    [GeneratedRegex(@"public\s+static\s+void\s+ClearCache\s*\(")]
    private static partial Regex StaticClearCacheRegex();

    [Fact]
    public void NoSourceFile_HoldsItsOwnStaticMemoryCache()
    {
        StaticMemoryCacheRegex().IsMatch("private static readonly MemoryCache _principalCache = new(...);")
            .Should().BeTrue("the guard regex must match its own exemplar");

        var offenders = ScanSources(StaticMemoryCacheRegex());

        offenders.Should().BeEmpty(
            "a service or middleware does not own a process-wide MemoryCache: inject "
            + "SingleFlightCache and prefix your keys. A static cache cannot be emptied by a "
            + "test host and cannot be sized by a product. Offenders:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoSourceFile_HandRollsACacheDictionary()
    {
        StaticCacheDictionaryRegex().IsMatch(
            "private static readonly ConcurrentDictionary<string, TenantResolverCacheEntry> _cache = new();")
            .Should().BeTrue("the guard regex must match its own exemplar");

        var offenders = ScanSources(StaticCacheDictionaryRegex());

        offenders.Should().BeEmpty(
            "a ConcurrentDictionary of cache entries re-implements expiry and eviction that "
            + "SingleFlightCache already has. Inject it instead. Offenders:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoSourceFile_ExposesAStaticClearCache()
    {
        StaticClearCacheRegex().IsMatch("public static void ClearCache()")
            .Should().BeTrue("the guard regex must match its own exemplar");

        var offenders = ScanSources(StaticClearCacheRegex());

        offenders.Should().BeEmpty(
            "a public static ClearCache() is production surface that exists for the tests. A "
            + "test host empties its own IMemoryCache instead (FoundationWebApplicationFactory."
            + "ResetCaches). Offenders:\n  " + string.Join("\n  ", offenders));
    }

    private static List<string> ScanSources(Regex forbidden)
    {
        var results = new List<string>();
        foreach (var sub in new[] { "src", "core" })
        {
            var dir = TestRepoPaths.FindDirectory("backend", sub);
            dir.Should().NotBeNull($"could not locate backend/{sub}");

            var files = Directory.GetFiles(dir!, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToList();
            files.Should().NotBeEmpty($"the source scan found no .cs files under backend/{sub} — did the layout move?");

            results.AddRange(files
                .Select(f => (Key: $"{sub}:{Path.GetRelativePath(dir!, f).Replace('\\', '/')}", Text: File.ReadAllText(f)))
                .Where(x => !ExemptFiles.Contains(x.Key) && forbidden.IsMatch(x.Text))
                .Select(x => x.Key));
        }
        return results.Order(StringComparer.Ordinal).ToList();
    }
}
