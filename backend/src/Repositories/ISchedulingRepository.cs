using Api.Models;

namespace Api.Repositories;

public interface ISchedulingRepository
{
    // ── Scheduling Settings ─────────────────────────────────────────
    Task<SchedulingSettingsInfo?> GetSettingsAsync(Guid siteId);
    Task<SchedulingSettingsInfo> UpsertSettingsAsync(Guid siteId, UpsertSchedulingSettingsRequest request);
    Task<bool> DeleteSettingsAsync(Guid siteId);

    // ── Helpers ──────────────────────────────────────────────────────
    Task<Guid?> GetSiteIdForSpaceAsync(Guid spaceId);

    // ── Off-Times ───────────────────────────────────────────────────
    Task<List<OffTimeInfo>> GetOffTimesAsync(Guid siteId);
    Task<OffTimeInfo?> GetOffTimeByIdAsync(Guid siteId, Guid offTimeId);
    Task<OffTimeInfo> CreateOffTimeAsync(Guid siteId, CreateOffTimeRequest request);
    Task<OffTimeInfo?> UpdateOffTimeAsync(Guid siteId, Guid offTimeId, UpdateOffTimeRequest request);
    Task<bool> DeleteOffTimeAsync(Guid siteId, Guid offTimeId);
}
