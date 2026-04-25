using Api.Models;

namespace Api.Services;

public interface IAnnouncementService
{
    Task<List<AnnouncementDto>> GetAllAsync(bool includeExpired = false);
    Task<AnnouncementDto?> GetByIdAsync(Guid id);
    Task<AnnouncementDto> CreateAsync(CreateAnnouncementRequest request, Guid userId);
    Task<AnnouncementDto?> UpdateAsync(Guid id, UpdateAnnouncementRequest request, Guid userId);
    Task<bool> DeleteAsync(Guid id);
    Task<List<UserAnnouncementDto>> GetActiveForUserAsync(Guid userId);
    Task<int> GetUnreadCountAsync(Guid userId);
    Task MarkReadAsync(Guid announcementId, Guid userId);
}
