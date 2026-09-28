using Api.Models;

namespace Api.Repositories;

public interface IAvailabilityEventRepository
{
    Task<List<AvailabilityEventInfo>> GetBySiteAsync(Guid siteId, CancellationToken ct = default);
    /// <summary>Bulk fetch events (with scopes) for many sites in one pass, keyed by site id — for export.</summary>
    Task<Dictionary<Guid, List<AvailabilityEventInfo>>> GetBySitesAsync(IReadOnlyList<Guid> siteIds, CancellationToken ct = default);
    // Every by-id method takes the site the caller addressed: an event of another site is not
    // found (null / false), so the parent-scope rule lives here rather than in each caller.
    Task<AvailabilityEventInfo?> GetByIdAsync(Guid siteId, Guid id, CancellationToken ct = default);
    Task<AvailabilityEventInfo> CreateAsync(Guid siteId, CreateAvailabilityEventRequest request, CancellationToken ct = default);
    Task<AvailabilityEventInfo?> UpdateAsync(Guid siteId, Guid id, UpdateAvailabilityEventRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid siteId, Guid id, CancellationToken ct = default);

    /// <summary>Null when the event is not in the site.</summary>
    Task<AvailabilityEventScopeInfo?> AddScopeAsync(Guid siteId, Guid eventId, AddScopeRequest request, CancellationToken ct = default);
    Task<bool> DeleteScopeAsync(Guid siteId, Guid eventId, Guid scopeId, CancellationToken ct = default);

    /// <summary>Returns all enabled events for the site, with their scopes loaded. Used by the availability resolver.</summary>
    Task<List<AvailabilityEventInfo>> GetEnabledBySiteWithScopesAsync(Guid siteId, CancellationToken ct = default);
}
