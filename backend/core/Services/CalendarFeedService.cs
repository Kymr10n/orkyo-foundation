using System.Security.Cryptography;
using System.Text;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface ICalendarFeedService
{
    /// <summary>Hashes a presented token the same way stored ones are hashed.</summary>
    string HashToken(string token);

    /// <summary>A fresh, URL-safe subscription token. Returned once, stored only as a hash.</summary>
    string GenerateToken();

    /// <summary>
    /// The events a subscription should serve: scheduled requests inside the feed
    /// window, optionally narrowed to one site.
    /// </summary>
    Task<List<CalendarFeedEvent>> GetEventsAsync(Guid? siteId, DateTime nowUtc, CancellationToken ct = default);
}

public class CalendarFeedService(
    IRequestRepository requestRepo,
    IResourceRepository resourceRepo) : ICalendarFeedService
{
    /// <summary>
    /// How much of the schedule a subscriber sees. Wide enough to plan against,
    /// bounded so a long-lived tenant's whole history is not re-sent every hour —
    /// calendar clients refetch the entire document, every time.
    /// </summary>
    private const int MonthsBack = 3;
    private const int MonthsForward = 12;

    public string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public string GenerateToken() =>
        // 256 bits, base64url — unguessable, and safe in a URL path segment.
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public async Task<List<CalendarFeedEvent>> GetEventsAsync(
        Guid? siteId, DateTime nowUtc, CancellationToken ct = default)
    {
        var from = nowUtc.AddMonths(-MonthsBack);
        var to = nowUtc.AddMonths(MonthsForward);

        var requests = await requestRepo.GetScheduledAsync(from, to, ct);
        if (siteId.HasValue)
        {
            // Site-neutral requests (SiteId null) are deliberately included: they are
            // scheduled work the subscriber is expected to do, wherever it happens.
            requests = requests.Where(r => r.SiteId == siteId || r.SiteId is null).ToList();
        }
        if (requests.Count == 0) return [];

        // One lookup for every assigned resource, so LOCATION can name them.
        var resourceIds = requests
            .SelectMany(r => r.Assignments.Select(a => a.ResourceId))
            .Distinct()
            .ToList();
        var namesById = (await resourceRepo.GetByIdsAsync(resourceIds, ct))
            .ToDictionary(r => r.Id, r => r.Name);

        return requests
            .Where(r => r.StartTs.HasValue && r.EndTs.HasValue)
            .OrderBy(r => r.StartTs!.Value)
            .Select(r => new CalendarFeedEvent
            {
                Id = r.Id,
                Summary = r.Name,
                Description = r.Description,
                StartUtc = r.StartTs!.Value,
                EndUtc = r.EndTs!.Value,
                Location = string.Join(", ", r.Assignments
                    .Select(a => namesById.GetValueOrDefault(a.ResourceId))
                    .Where(n => !string.IsNullOrWhiteSpace(n))),
                LastModifiedUtc = r.UpdatedAt,
            })
            .ToList();
    }
}
