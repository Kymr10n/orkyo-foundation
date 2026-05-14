using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface IOffTimeResourceQuery
{
    Task<List<OffTimeInfo>> GetBlockingOffTimesAsync(Guid resourceId, Guid siteId, DateTime startUtc, DateTime endUtc);
}

/// <summary>
/// Phase-1 read-only adapter: surfaces the existing off_times / off_time_spaces
/// data through a resource-id lens by leveraging the fact that spaces.id and
/// resources.id share the same uuid after Phase 2. During Phase 1 (before
/// resources are linked to spaces) only site-wide off-times (applies_to_all_spaces=true)
/// are returned; per-space off-times require the space↔resource link.
/// Phase 2 replaces this implementation with a direct read of off_time_resources.
/// </summary>
public class OffTimeResourceQuery(ISchedulingRepository schedulingRepository) : IOffTimeResourceQuery
{
    public async Task<List<OffTimeInfo>> GetBlockingOffTimesAsync(
        Guid resourceId, Guid siteId, DateTime startUtc, DateTime endUtc)
    {
        var all = await schedulingRepository.GetOffTimesAsync(siteId);

        return all.Where(ot =>
            ot.Enabled &&
            ot.StartTs < endUtc &&
            ot.EndTs > startUtc &&
            (ot.AppliesToAllSpaces || (ot.SpaceIds != null && ot.SpaceIds.Contains(resourceId)))
        ).ToList();
    }
}
