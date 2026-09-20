using Microsoft.Extensions.Caching.Memory;

namespace Api.Services.Caching;

/// <summary>
/// The cache for analytics payloads — insights overviews and conflict timelines.
/// </summary>
/// <remarks>
/// <para>
/// A separate instance over its own bounded <see cref="IMemoryCache"/>, not the shared one. Two
/// properties make these entries different from the identity entries the shared cache holds.
/// They are large — a whole overview object or a list of conflict points rather than a GUID or a
/// role — and their keys are high-cardinality, since they include the window's start and end
/// ticks, so scrubbing a dashboard across date ranges mints a new entry per range.
/// </para>
/// <para>
/// Before the caching consolidation these lived behind a cache with a 10,000-entry cap. The
/// consolidation dropped every per-cache cap in favour of TTL-only bounding, which is right for
/// identity and wrong here. <see cref="EntryLimit"/> restores the old bound on this instance
/// alone. A limit on the SHARED cache would be the wrong fix: it would force every product that
/// writes to the injected IMemoryCache to set a Size on the entry or get an exception.
/// </para>
/// </remarks>
public sealed class AnalyticsCache : SingleFlightCache
{
    /// <summary>Maximum entries. Each write carries Size = 1, so this is an entry count.</summary>
    public const int EntryLimit = 10_000;

    public AnalyticsCache()
        : base(new MemoryCache(new MemoryCacheOptions { SizeLimit = EntryLimit }))
    {
    }
}
