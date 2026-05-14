using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface ISchedulingService
{
    Task<SchedulingSettingsInfo?> GetSettingsAsync(Guid siteId);
    Task<SchedulingSettingsInfo> UpsertSettingsAsync(Guid siteId, UpsertSchedulingSettingsRequest request);
    Task<bool> DeleteSettingsAsync(Guid siteId);
    Task<List<OffTimeInfo>> GetOffTimesAsync(Guid siteId);
    Task<OffTimeInfo?> GetOffTimeByIdAsync(Guid siteId, Guid offTimeId);
    Task<OffTimeInfo> CreateOffTimeAsync(Guid siteId, CreateOffTimeRequest request);
    Task<OffTimeInfo?> UpdateOffTimeAsync(Guid siteId, Guid offTimeId, UpdateOffTimeRequest request);
    Task<bool> DeleteOffTimeAsync(Guid siteId, Guid offTimeId);
    Task RecalculateScheduledRequestsAsync(Guid siteId);
    Task<CreateRequestRequest> ApplySchedulingToCreateAsync(CreateRequestRequest request);
    Task<UpdateRequestRequest> ApplySchedulingToUpdateAsync(Guid requestId, UpdateRequestRequest request);
    Task<ScheduleRequestRequest> ApplySchedulingToScheduleAsync(Guid requestId, ScheduleRequestRequest request);
}

public class SchedulingService : ISchedulingService
{
    private readonly ISchedulingRepository _schedulingRepository;
    private readonly IRequestRepository _requestRepository;
    private readonly ILogger<SchedulingService> _logger;

    public SchedulingService(
        ISchedulingRepository schedulingRepository,
        IRequestRepository requestRepository,
        ILogger<SchedulingService> logger)
    {
        _schedulingRepository = schedulingRepository;
        _requestRepository = requestRepository;
        _logger = logger;
    }

    public Task<SchedulingSettingsInfo?> GetSettingsAsync(Guid siteId)
        => _schedulingRepository.GetSettingsAsync(siteId);

    public Task<SchedulingSettingsInfo> UpsertSettingsAsync(Guid siteId, UpsertSchedulingSettingsRequest request)
        => _schedulingRepository.UpsertSettingsAsync(siteId, request);

    public Task<bool> DeleteSettingsAsync(Guid siteId)
        => _schedulingRepository.DeleteSettingsAsync(siteId);

    public Task<List<OffTimeInfo>> GetOffTimesAsync(Guid siteId)
        => _schedulingRepository.GetOffTimesAsync(siteId);

    public Task<OffTimeInfo?> GetOffTimeByIdAsync(Guid siteId, Guid offTimeId)
        => _schedulingRepository.GetOffTimeByIdAsync(siteId, offTimeId);

    public Task<OffTimeInfo> CreateOffTimeAsync(Guid siteId, CreateOffTimeRequest request)
        => _schedulingRepository.CreateOffTimeAsync(siteId, request);

    public Task<OffTimeInfo?> UpdateOffTimeAsync(Guid siteId, Guid offTimeId, UpdateOffTimeRequest request)
        => _schedulingRepository.UpdateOffTimeAsync(siteId, offTimeId, request);

    public Task<bool> DeleteOffTimeAsync(Guid siteId, Guid offTimeId)
        => _schedulingRepository.DeleteOffTimeAsync(siteId, offTimeId);

    public async Task RecalculateScheduledRequestsAsync(Guid siteId)
    {
        var settings = await _schedulingRepository.GetSettingsAsync(siteId)
            ?? SchedulingSettingsInfo.Default(siteId);
        var offTimes = await _schedulingRepository.GetOffTimesAsync(siteId);
        var toRecalculate = await _requestRepository.GetScheduledBySiteAsync(siteId);

        if (toRecalculate.Count == 0) return;

        _logger.LogInformation("Recalculating {Count} scheduled requests for site {SiteId}",
            toRecalculate.Count, siteId);

        var updates = new List<(Guid Id, ScheduleRequestRequest Data)>();
        foreach (var request in toRecalculate)
        {
            try
            {
                var durationMinutes = SchedulingEngine.DurationToMinutes(
                    request.MinimalDurationValue, request.MinimalDurationUnit);
                var result = SchedulingEngine.CalculateSchedule(
                    request.StartTs!.Value, durationMinutes, true, settings, offTimes);

                updates.Add((request.Id, new ScheduleRequestRequest
                {
                    ResourceId = request.PrimaryResourceId,
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
        {
            await _requestRepository.BatchUpdateSchedulesAsync(updates);
        }

        _logger.LogInformation("Recalculated {Count} requests for site {SiteId}",
            updates.Count, siteId);
    }

    public async Task<CreateRequestRequest> ApplySchedulingToCreateAsync(CreateRequestRequest request)
    {
        if (!request.SchedulingSettingsApply || request.ResourceId == null || request.StartTs == null)
            return request;

        var result = await ComputeScheduledTimesAsync(
            request.ResourceId.Value, request.StartTs.Value,
            request.MinimalDurationValue, request.MinimalDurationUnit);

        return result == null ? request : request with
        {
            StartTs = result.ActualStart,
            EndTs = result.ActualEnd,
            ActualDurationValue = result.ActualDurationMinutes,
            ActualDurationUnit = DurationUnit.Minutes
        };
    }

    public async Task<UpdateRequestRequest> ApplySchedulingToUpdateAsync(
        Guid requestId, UpdateRequestRequest request)
    {
        var existing = await _requestRepository.GetByIdAsync(requestId);
        if (existing == null) return request;

        var applyScheduling = request.SchedulingSettingsApply ?? existing.SchedulingSettingsApply;
        if (!applyScheduling) return request;

        var resourceId = request.ResourceId ?? existing.PrimaryResourceId;
        var startTs = request.StartTs ?? existing.StartTs;
        if (resourceId == null || startTs == null) return request;

        var result = await ComputeScheduledTimesAsync(
            resourceId.Value, startTs.Value,
            request.MinimalDurationValue ?? existing.MinimalDurationValue,
            request.MinimalDurationUnit ?? existing.MinimalDurationUnit);

        return result == null ? request : request with
        {
            StartTs = result.ActualStart,
            EndTs = result.ActualEnd,
            ActualDurationValue = result.ActualDurationMinutes,
            ActualDurationUnit = DurationUnit.Minutes
        };
    }

    public async Task<ScheduleRequestRequest> ApplySchedulingToScheduleAsync(
        Guid requestId, ScheduleRequestRequest request)
    {
        if (request.ResourceId == null || request.StartTs == null) return request;

        var existing = await _requestRepository.GetByIdAsync(requestId);
        if (existing == null || !existing.SchedulingSettingsApply) return request;

        // When the caller supplied an explicit EndTs (same-space resize or
        // cross-space reschedule with explicit times), honour the provided
        // timestamps instead of recalculating from the minimal duration.
        if (request.EndTs != null) return request;

        var result = await ComputeScheduledTimesAsync(
            request.ResourceId.Value, request.StartTs.Value,
            existing.MinimalDurationValue, existing.MinimalDurationUnit);

        return result == null ? request : request with
        {
            StartTs = result.ActualStart,
            EndTs = result.ActualEnd,
            ActualDurationValue = result.ActualDurationMinutes,
            ActualDurationUnit = DurationUnit.Minutes
        };
    }

    private async Task<(SchedulingSettingsInfo Settings, List<OffTimeInfo> OffTimes)?> LoadSchedulingContextAsync(
        Guid resourceId)
    {
        var siteId = await _schedulingRepository.GetSiteIdForResourceAsync(resourceId);
        if (siteId == null) return null;

        var settings = await _schedulingRepository.GetSettingsAsync(siteId.Value)
            ?? SchedulingSettingsInfo.Default(siteId.Value);

        var offTimes = await _schedulingRepository.GetOffTimesAsync(siteId.Value);
        return (settings, offTimes);
    }

    private async Task<SchedulingEngine.ScheduleResult?> ComputeScheduledTimesAsync(
        Guid resourceId, DateTime startTs, int durationValue, DurationUnit durationUnit)
    {
        var ctx = await LoadSchedulingContextAsync(resourceId);
        if (ctx == null) return null;

        var durationMinutes = SchedulingEngine.DurationToMinutes(durationValue, durationUnit);
        return SchedulingEngine.CalculateSchedule(
            startTs, durationMinutes, true, ctx.Value.Settings, ctx.Value.OffTimes);
    }
}
