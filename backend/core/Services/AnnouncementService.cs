using Api.Models;
using Api.Repositories;

namespace Api.Services;

public class AnnouncementService : IAnnouncementService
{
    private const int DefaultRetentionDays = 90;
    private const int MaxTitleLength = 200;
    private const int MaxBodyLength = 5000;

    private readonly IAnnouncementRepository _repository;

    public AnnouncementService(IAnnouncementRepository repository)
    {
        _repository = repository;
    }

    public Task<List<AnnouncementDto>> GetAllAsync(bool includeExpired = false, CancellationToken ct = default)
        => _repository.GetAllAsync(includeExpired, ct);

    public Task<AnnouncementDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _repository.GetByIdAsync(id, ct);

    public async Task<AnnouncementDto> CreateAsync(CreateAnnouncementRequest request, Guid userId, CancellationToken ct = default)
    {
        var error = Validate(request.Title, request.Body);
        if (error != null)
            throw new ArgumentException(error);

        var retentionDays = request.RetentionDays ?? DefaultRetentionDays;
        if (retentionDays < 1 || retentionDays > 3650)
            throw new ArgumentException("Retention must be between 1 and 3650 days.");

        var announcement = new Announcement
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            IsImportant = request.IsImportant,
            Revision = 1,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            ExpiresAt = DateTime.UtcNow.AddDays(retentionDays),
        };

        return await _repository.CreateAsync(announcement);
    }

    public async Task<AnnouncementDto?> UpdateAsync(Guid id, UpdateAnnouncementRequest request, Guid userId, CancellationToken ct = default)
    {
        var error = Validate(request.Title, request.Body);
        if (error != null)
            throw new ArgumentException(error);

        if (request.ExpiresAt.HasValue && request.ExpiresAt.Value <= DateTime.UtcNow)
            throw new ArgumentException("Expiration date must be in the future.");

        return await _repository.UpdateAsync(id, request.Title.Trim(), request.Body.Trim(),
            request.IsImportant, request.ExpiresAt, userId);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => _repository.DeleteAsync(id, ct);
    public Task<List<UserAnnouncementDto>> GetActiveForUserAsync(Guid userId, CancellationToken ct = default) => _repository.GetActiveForUserAsync(userId, ct);
    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default) => _repository.GetUnreadCountAsync(userId, ct);
    public Task MarkReadAsync(Guid announcementId, Guid userId, CancellationToken ct = default) => _repository.MarkReadAsync(announcementId, userId, ct);

    private static string? Validate(string title, string body)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Title is required.";
        if (title.Length > MaxTitleLength) return $"Title must be {MaxTitleLength} characters or fewer.";
        if (string.IsNullOrWhiteSpace(body)) return "Body is required.";
        if (body.Length > MaxBodyLength) return $"Body must be {MaxBodyLength} characters or fewer.";
        return null;
    }
}
