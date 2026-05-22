using Api.Models;

namespace Api.Repositories;

public interface IAvailabilityEventRepository
{
    Task<List<AvailabilityEventInfo>> GetBySiteAsync(Guid siteId, CancellationToken ct = default);
    Task<AvailabilityEventInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AvailabilityEventInfo> CreateAsync(Guid siteId, CreateAvailabilityEventRequest request, CancellationToken ct = default);
    Task<AvailabilityEventInfo?> UpdateAsync(Guid id, UpdateAvailabilityEventRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<AvailabilityEventScopeInfo> AddScopeAsync(Guid eventId, AddScopeRequest request, CancellationToken ct = default);
    Task<bool> DeleteScopeAsync(Guid eventId, Guid scopeId, CancellationToken ct = default);

    /// <summary>Returns all enabled events for the site, with their scopes loaded. Used by the availability resolver.</summary>
    Task<List<AvailabilityEventInfo>> GetEnabledBySiteWithScopesAsync(Guid siteId, CancellationToken ct = default);
}
