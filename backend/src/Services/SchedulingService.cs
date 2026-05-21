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
    private readonly IAvailabilityResolver _resolver;
    private readonly ILogger<SchedulingService> _logger;

    public SchedulingService(
        ISchedulingRepository schedulingRepository,
        IRequestRepository requestRepository,
        IAvailabilityResolver resolver,
        ILogger<SchedulingService> logger)
    {
        _schedulingRepository = schedulingRepository;
        _requestRepository = requestRepository;
        _resolver = resolver;
        _logger = logger;
    }

    public Task<SchedulingSettingsInfo?> GetSettingsAsync(Guid siteId, CancellationToken ct = default)
        => _schedulingRepository.GetSettingsAsync(siteId, ct);

    public Task<SchedulingSettingsInfo> UpsertSettingsAsync(Guid siteId, UpsertSchedulingSettingsRequest request, CancellationToken ct = default)
        => _schedulingRepository.UpsertSettingsAsync(siteId, request, ct);

    public Task<bool> DeleteSettingsAsync(Guid siteId, CancellationToken ct = default)
        => _schedulingRepository.DeleteSettingsAsync(siteId, ct);

    public async Task RecalculateScheduledRequestsAsync(Guid siteId, CancellationToken ct = default)
    {
        var settings = await _schedulingRepository.GetSettingsAsync(siteId)
            ?? SchedulingSettingsInfo.Default(siteId);
        var toRecalculate = await _requestRepository.GetScheduledBySiteAsync(siteId);

        if (toRecalculate.Count == 0) return;

        _logger.LogInformation("Recalculating {Count} scheduled requests for site {SiteId}",
            toRecalculate.Count, siteId);

        var updates = new List<(Guid Id, ScheduleRequestRequest Data)>();
        foreach (var request in toRecalculate)
        {
            try
            {
                var resourceId = request.GetSpaceResourceId();
                var blockedPeriods = resourceId.HasValue
                    ? await _resolver.GetBlockedPeriodsAsync(resourceId.Value, ct)
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
            await _requestRepository.BatchUpdateSchedulesAsync(updates);

        _logger.LogInformation("Recalculated {Count} requests for site {SiteId}",
            updates.Count, siteId);
    }

    public async Task<CreateRequestRequest> ApplySchedulingToCreateAsync(CreateRequestRequest request, CancellationToken ct = default)
    {
        if (!request.SchedulingSettingsApply || request.ResourceId == null || request.StartTs == null)
            return request;

        var result = await ComputeScheduledTimesAsync(
            request.ResourceId.Value, request.StartTs.Value,
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
        var existing = await _requestRepository.GetByIdAsync(requestId);
        if (existing == null) return request;

        var applyScheduling = request.SchedulingSettingsApply ?? existing.SchedulingSettingsApply;
        if (!applyScheduling) return request;

        var resourceId = request.ResourceId ?? existing.GetSpaceResourceId();
        var startTs = request.StartTs ?? existing.StartTs;
        if (resourceId == null || startTs == null) return request;

        var result = await ComputeScheduledTimesAsync(
            resourceId.Value, startTs.Value,
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

        var existing = await _requestRepository.GetByIdAsync(requestId);
        if (existing == null || !existing.SchedulingSettingsApply) return request;

        if (request.EndTs != null) return request;

        var result = await ComputeScheduledTimesAsync(
            request.ResourceId.Value, request.StartTs.Value,
            existing.MinimalDurationValue, existing.MinimalDurationUnit, ct);

        return result == null ? request : request with
        {
            StartTs = result.ActualStart,
            EndTs = result.ActualEnd,
            ActualDurationValue = result.ActualDurationMinutes,
            ActualDurationUnit = DurationUnit.Minutes
        };
    }

    private async Task<SchedulingEngine.ScheduleResult?> ComputeScheduledTimesAsync(
        Guid resourceId, DateTime startTs, int durationValue, DurationUnit durationUnit, CancellationToken ct = default)
    {
        var siteId = await _schedulingRepository.GetSiteIdForResourceAsync(resourceId, ct);
        if (siteId == null) return null;

        var settings = await _schedulingRepository.GetSettingsAsync(siteId.Value, ct)
            ?? SchedulingSettingsInfo.Default(siteId.Value);

        var blockedPeriods = await _resolver.GetBlockedPeriodsAsync(resourceId, ct);
        var durationMinutes = SchedulingEngine.DurationToMinutes(durationValue, durationUnit);
        return SchedulingEngine.CalculateSchedule(startTs, durationMinutes, true, settings, blockedPeriods);
    }
}
