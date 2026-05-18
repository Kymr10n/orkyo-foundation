using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface IOffTimeResourceQuery
{
    Task<List<OffTimeInfo>> GetBlockingOffTimesAsync(Guid resourceId, Guid siteId, DateTime startUtc, DateTime endUtc, CancellationToken ct = default);
}

/// <summary>
/// Queries off-times that block a specific resource.
/// Returns off-times where AppliesToAllResources=true OR the resource is in ResourceIds.
/// </summary>
public class OffTimeResourceQuery(ISchedulingRepository schedulingRepository) : IOffTimeResourceQuery
{
    public async Task<List<OffTimeInfo>> GetBlockingOffTimesAsync(
        Guid resourceId, Guid siteId, DateTime startUtc, DateTime endUtc, CancellationToken ct = default)
    {
        var all = await schedulingRepository.GetOffTimesAsync(siteId);

        return all.Where(ot =>
            ot.Enabled &&
            ot.StartTs < endUtc &&
            ot.EndTs > startUtc &&
            (ot.AppliesToAllResources || (ot.ResourceIds != null && ot.ResourceIds.Contains(resourceId)))
        ).ToList();
    }
}
