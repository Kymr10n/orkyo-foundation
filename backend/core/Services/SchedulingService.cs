using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface ISchedulingService
{
    Task<SchedulingSettingsInfo?> GetSettingsAsync(Guid siteId, CancellationToken ct = default);
    Task<SchedulingSettingsInfo> UpsertSettingsAsync(Guid siteId, UpsertSchedulingSettingsRequest request, CancellationToken ct = default);
    Task<bool> DeleteSettingsAsync(Guid siteId, CancellationToken ct = default);
    Task RecalculateScheduledRequestsAsync(Guid siteId, CancellationToken ct = default);
    Task<CreateRequestRequest> ApplySchedulingToCreateAsync(CreateRequestRequest request, CancellationToken ct = default);
    Task<UpdateRequestRequest> ApplySchedulingToUpdateAsync(Guid requestId, UpdateRequestRequest request, CancellationToken ct = default);
    Task<ScheduleRequestRequest> ApplySchedulingToScheduleAsync(Guid requestId, ScheduleRequestRequest request, CancellationToken ct = default);
}

public class SchedulingService : ISchedulingService
{
    private readonly ISchedulingRepository _schedulingRepository;
    private readonly IRequestRepository _requestRepository;
    private readonly IResourceTypeRepository _resourceTypeRepository;
    private readonly IResourceRepository _resourceRepository;
    private readonly IAvailabilityResolver _resolver;
    private readonly ILogger<SchedulingService> _logger;

    public SchedulingService(
        ISchedulingRepository schedulingRepository,
        IRequestRepository requestRepository,
        IResourceTypeRepository resourceTypeRepository,
        IResourceRepository resourceRepository,
        IAvailabilityResolver resolver,
        ILogger<SchedulingService> logger)
    {
        _schedulingRepository = schedulingRepository;
        _requestRepository = requestRepository;
        _resourceTypeRepository = resourceTypeRepository;
        _resourceRepository = resourceRepository;
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>
    /// The resource whose site the working-hours adjustment should follow: the first that
    /// cannot travel. An immovable resource fixes where the work happens; a person or a van
    /// carries a home site that says nothing about where this request runs, so picking one of
    /// those would resolve settings from an unrelated site. Falls back to null when nothing on
    /// the request is anchored, in which case no adjustment is made.
    /// </summary>
    private async Task<Guid?> ResolveSiteBearingResourceAsync(
        IReadOnlyList<Guid> resourceIds, CancellationToken ct)
    {
        if (resourceIds.Count == 0) return null;

        var resources = await _resourceRepository.GetByIdsAsync(resourceIds, ct);
        return resources.FirstOrDefault(r => !r.CrossSiteAllowed)?.Id;
    }

    public Task<SchedulingSettingsInfo?> GetSettingsAsync(Guid siteId, CancellationToken ct = default)
        => _schedulingRepository.GetSettingsAsync(siteId, ct);

    public Task<SchedulingSettingsInfo> UpsertSettingsAsync(Guid siteId, UpsertSchedulingSettingsRequest request, CancellationToken ct = default)
        => _schedulingRepository.UpsertSettingsAsync(siteId, request, ct);

    public Task<bool> DeleteSettingsAsync(Guid siteId, CancellationToken ct = default)
        => _schedulingRepository.DeleteSettingsAsync(siteId, ct);

    public async Task RecalculateScheduledRequestsAsync(Guid siteId, CancellationToken ct = default)
    {
        var settings = await _schedulingRepository.GetSettingsAsync(siteId, ct)
            ?? SchedulingSettingsInfo.Default(siteId);
        var toRecalculate = await _requestRepository.GetScheduledBySiteAsync(siteId, ct);

        if (toRecalculate.Count == 0) return;

        _logger.LogInformation("Recalculating {Count} scheduled requests for site {SiteId}",
            toRecalculate.Count, siteId);

        // The placement can be any placeable type, not only space — a request recalculated on a
        // mill used to get an empty blocked-period set here and drift over its machine's off-time.
        var placeableKeys = (await _resourceTypeRepository.GetPlaceableKeysAsync(ct)).ToHashSet();
        var placementResourceIds = toRecalculate
            .Select(r => r.GetPlacementResourceId(placeableKeys))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var blockedByResource = await _resolver.GetBlockedPeriodsForResourcesAsync(placementResourceIds, ct);

        var updates = new List<(Guid Id, ScheduleRequestRequest Data)>();
        foreach (var request in toRecalculate)
        {
            try
            {
                var resourceId = request.GetPlacementResourceId(placeableKeys);
                var blockedPeriods = resourceId.HasValue
                    ? blockedByResource[resourceId.Value]
                    : [];

                var durationMinutes = SchedulingEngine.DurationToMinutes(
                    request.MinimalDurationValue, request.MinimalDurationUnit);
                var result = SchedulingEngine.CalculateSchedule(
                    request.StartTs!.Value, durationMinutes, true, settings, blockedPeriods);

                updates.Add((request.Id, new ScheduleRequestRequest
                {
                    ResourceId = resourceId,
                    StartTs = result.ActualStart,
                    EndTs = result.ActualEnd,
                    ActualDurationValue = result.ActualDurationMinutes,
                    ActualDurationUnit = DurationUnit.Minutes
                }));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to recalculate request {RequestId}", request.Id);
            }
        }

        if (updates.Count > 0)
            await _requestRepository.BatchUpdateSchedulesAsync(updates, ct);

        _logger.LogInformation("Recalculated {Count} requests for site {SiteId}",
            updates.Count, siteId);
    }

    public async Task<CreateRequestRequest> ApplySchedulingToCreateAsync(CreateRequestRequest request, CancellationToken ct = default)
    {
        if (!request.SchedulingSettingsApply || request.StartTs == null)
            return request;

        // Respect an explicitly-provided window, exactly as the update path does: when the caller
        // sets an end, never override it. A window that is too short or sits on off-time persists
        // and surfaces a conflict — a red flag beats a silently moved plan. The auto-compute below
        // only applies when the caller gives a start but no end.
        if (request.EndTs != null) return request;

        var resourceIds = request.ResourceIds ?? [];
        var siteBearer = await ResolveSiteBearingResourceAsync(resourceIds, ct);
        if (siteBearer is null) return request;

        var result = await ComputeScheduledTimesAsync(
            siteBearer.Value, resourceIds, request.StartTs.Value,
            request.MinimalDurationValue, request.MinimalDurationUnit, ct);

        return result == null ? request : request with
        {
            StartTs = result.ActualStart,
            EndTs = result.ActualEnd,
            ActualDurationValue = result.ActualDurationMinutes,
            ActualDurationUnit = DurationUnit.Minutes
        };
    }

    public async Task<UpdateRequestRequest> ApplySchedulingToUpdateAsync(
        Guid requestId, UpdateRequestRequest request, CancellationToken ct = default)
    {
        var existing = await _requestRepository.GetByIdAsync(requestId, ct: ct);
        if (existing == null) return request;

        var applyScheduling = request.SchedulingSettingsApply ?? existing.SchedulingSettingsApply;
        if (!applyScheduling) return request;

        // Respect an explicitly-provided window: when the caller sets an end, never override it.
        // A window shorter than the minimal duration is allowed to persist and surfaces a
        // `below_min_duration` conflict (ConflictService) — matching the edit dialog's promise.
        // The auto-compute below only applies when the caller gives a start but no end.
        if (request.EndTs != null) return request;

        // Not FirstOrDefault over the assignments: the view orders them by type key, so a
        // request holding a person and a room resolved its working hours from the person's home
        // office, alphabetically. Only an immovable resource says where the work happens.
        var resourceIds = request.ResourceIds ?? [.. existing.Assignments.Select(a => a.ResourceId)];
        var resourceId = await ResolveSiteBearingResourceAsync(resourceIds, ct);
        var startTs = request.StartTs ?? existing.StartTs;
        if (resourceId == null || startTs == null) return request;

        var result = await ComputeScheduledTimesAsync(
            resourceId.Value, resourceIds, startTs.Value,
            request.MinimalDurationValue ?? existing.MinimalDurationValue,
            request.MinimalDurationUnit ?? existing.MinimalDurationUnit, ct);

        return result == null ? request : request with
        {
            StartTs = result.ActualStart,
            EndTs = result.ActualEnd,
            ActualDurationValue = result.ActualDurationMinutes,
            ActualDurationUnit = DurationUnit.Minutes
        };
    }

    public async Task<ScheduleRequestRequest> ApplySchedulingToScheduleAsync(
        Guid requestId, ScheduleRequestRequest request, CancellationToken ct = default)
    {
        if (request.ResourceId == null || request.StartTs == null) return request;

        var existing = await _requestRepository.GetByIdAsync(requestId, ct: ct);
        if (existing == null || !existing.SchedulingSettingsApply) return request;

        if (request.EndTs != null) return request;

        // Only the resource being scheduled onto: this call rewrites that resource type's
        // assignment and leaves the others in place, so constraining the slot by resources this
        // drag may be replacing would push the request away from where it was dropped.
        var result = await ComputeScheduledTimesAsync(
            request.ResourceId.Value, [request.ResourceId.Value], request.StartTs.Value,
            existing.MinimalDurationValue, existing.MinimalDurationUnit, ct);

        return result == null ? request : request with
        {
            StartTs = result.ActualStart,
            EndTs = result.ActualEnd,
            ActualDurationValue = result.ActualDurationMinutes,
            ActualDurationUnit = DurationUnit.Minutes
        };
    }

    /// <summary>
    /// Working hours come from the site of <paramref name="siteBearingResourceId"/> — the one
    /// resource that says where the work happens. Blocked periods come from every assigned
    /// resource: a slot the machine is free for is no good if the operator is on leave, and
    /// landing the request there would create a conflict the moment it is saved.
    /// </summary>
    private async Task<SchedulingEngine.ScheduleResult?> ComputeScheduledTimesAsync(
        Guid siteBearingResourceId, IReadOnlyList<Guid> allResourceIds,
        DateTime startTs, int durationValue, DurationUnit durationUnit, CancellationToken ct = default)
    {
        var siteId = await _schedulingRepository.GetSiteIdForResourceAsync(siteBearingResourceId, ct);
        if (siteId == null) return null;

        var settings = await _schedulingRepository.GetSettingsAsync(siteId.Value, ct)
            ?? SchedulingSettingsInfo.Default(siteId.Value);

        var blockedByResource = await _resolver.GetBlockedPeriodsForResourcesAsync(allResourceIds, ct);
        var blockedPeriods = blockedByResource.Values.SelectMany(p => p).ToList();
        var durationMinutes = SchedulingEngine.DurationToMinutes(durationValue, durationUnit);
        return SchedulingEngine.CalculateSchedule(startTs, durationMinutes, true, settings, blockedPeriods);
    }
}
