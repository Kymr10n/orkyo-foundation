using Api.Models;
using Api.Repositories;

namespace Api.Services;

public class AnnouncementService : IAnnouncementService
{
    private const int DefaultRetentionDays = 90;

    private readonly IAnnouncementRepository _repository;
    private readonly TimeProvider _time;

    public AnnouncementService(IAnnouncementRepository repository, TimeProvider time)
    {
        _repository = repository;
        _time = time;
    }

    public Task<List<AnnouncementDto>> GetAllAsync(bool includeExpired = false, CancellationToken ct = default)
        => _repository.GetAllAsync(includeExpired, ct);

    public Task<AnnouncementDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _repository.GetByIdAsync(id, ct);

    // Title, body and retention shape: CreateAnnouncementRequestValidator, which every endpoint
    // runs before calling in.
    public async Task<AnnouncementDto> CreateAsync(CreateAnnouncementRequest request, Guid userId, CancellationToken ct = default)
    {
        var retentionDays = request.RetentionDays ?? DefaultRetentionDays;
        var channels = NormalizeChannels(request.Channels);

        var announcement = new Announcement
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            IsImportant = request.IsImportant,
            Channels = channels,
            Revision = 1,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            ExpiresAt = _time.GetUtcNow().UtcDateTime.AddDays(retentionDays),
        };

        return await _repository.CreateAsync(announcement, ct);
    }

    public async Task<AnnouncementDto?> UpdateAsync(Guid id, UpdateAnnouncementRequest request, Guid userId, CancellationToken ct = default)
    {
        if (request.ExpiresAt.HasValue && request.ExpiresAt.Value <= _time.GetUtcNow().UtcDateTime)
            throw new ArgumentException("Expiration date must be in the future.");

        return await _repository.UpdateAsync(id, request.Title.Trim(), request.Body.Trim(), request.IsImportant, request.ExpiresAt, userId, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => _repository.DeleteAsync(id, ct);
    public Task<List<UserAnnouncementDto>> GetActiveForUserAsync(Guid userId, CancellationToken ct = default) => _repository.GetActiveForUserAsync(userId, ct);
    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default) => _repository.GetUnreadCountAsync(userId, ct);
    public Task MarkReadAsync(Guid announcementId, Guid userId, CancellationToken ct = default) => _repository.MarkReadAsync(announcementId, userId, ct);

    /// <summary>Normalize + validate requested channels: lowercased, de-duped, non-empty subset of {site, email}.</summary>
    private static string[] NormalizeChannels(string[]? requested)
    {
        if (requested == null || requested.Length == 0)
            return AnnouncementChannels.Default;

        var channels = requested
            .Select(c => c?.Trim().ToLowerInvariant())
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .ToArray();

        if (channels.Length == 0)
            return AnnouncementChannels.Default;

        var invalid = channels.Where(c => !AnnouncementChannels.All.Contains(c!)).ToArray();
        if (invalid.Length > 0)
            throw new ArgumentException($"Unknown delivery channel(s): {string.Join(", ", invalid)}.");

        return channels!;
    }
}
