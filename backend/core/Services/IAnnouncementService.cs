using Api.Models;

namespace Api.Services;

/// <summary>
/// Manages system-wide announcements surfaced in the app UI.
/// Announcements can be time-bounded (expiry), targeted, and tracked per user.
/// </summary>
public interface IAnnouncementService
{
    /// <summary>Returns all announcements; pass <c>includeExpired: true</c> to include past ones.</summary>
    Task<List<AnnouncementDto>> GetAllAsync(bool includeExpired = false, CancellationToken ct = default);

    /// <summary>Returns the announcement with the given ID, or <c>null</c> if not found.</summary>
    Task<AnnouncementDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a new announcement authored by <paramref name="userId"/>.</summary>
    Task<AnnouncementDto> CreateAsync(CreateAnnouncementRequest request, Guid userId, CancellationToken ct = default);

    /// <summary>Updates an existing announcement. Returns <c>null</c> if not found.</summary>
    Task<AnnouncementDto?> UpdateAsync(Guid id, UpdateAnnouncementRequest request, Guid userId, CancellationToken ct = default);

    /// <summary>Deletes an announcement. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns non-expired announcements that the user has not yet dismissed.</summary>
    Task<List<UserAnnouncementDto>> GetActiveForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Returns the count of unread (active, not dismissed) announcements for the user.</summary>
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Marks the given announcement as read (dismissed) for the user.</summary>
    Task MarkReadAsync(Guid announcementId, Guid userId, CancellationToken ct = default);
}
