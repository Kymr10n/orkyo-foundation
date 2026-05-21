using Api.Models;

namespace Api.Services;

/// <summary>
/// Returns blocked periods for a specific resource in a given time window.
/// Used by <see cref="ResourceAssignmentValidator"/> to check availability before scheduling.
/// </summary>
public interface IOffTimeResourceQuery
{
    Task<List<BlockedPeriod>> GetBlockingPeriodsAsync(
        Guid resourceId, DateTime startUtc, DateTime endUtc, CancellationToken ct = default);
}

public class OffTimeResourceQuery(IAvailabilityResolver resolver) : IOffTimeResourceQuery
{
    public async Task<List<BlockedPeriod>> GetBlockingPeriodsAsync(
        Guid resourceId, DateTime startUtc, DateTime endUtc, CancellationToken ct = default)
    {
        var all = await resolver.GetBlockedPeriodsAsync(resourceId, ct);
        return all.Where(p => p.StartTs < endUtc && p.EndTs > startUtc).ToList();
    }
}
