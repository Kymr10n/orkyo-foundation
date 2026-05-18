using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Manages scheduling configuration (settings, off-times) for sites and applies
/// scheduling rules to request create/update/schedule operations.
/// </summary>
public interface ISchedulingService
{
    /// <summary>Returns scheduling settings for a site, or <c>null</c> if using defaults.</summary>
    Task<SchedulingSettingsInfo?> GetSettingsAsync(Guid siteId, CancellationToken ct = default);
    /// <summary>Creates or replaces scheduling settings for a site.</summary>
    Task<SchedulingSettingsInfo> UpsertSettingsAsync(Guid siteId, UpsertSchedulingSettingsRequest request, CancellationToken ct = default);
    /// <summary>Removes custom settings, reverting to defaults.</summary>
    Task<bool> DeleteSettingsAsync(Guid siteId, CancellationToken ct = default);
    /// <summary>Returns all off-time blocks for a site.</summary>
    Task<List<OffTimeInfo>> GetOffTimesAsync(Guid siteId, CancellationToken ct = default);
    /// <summary>Returns a specific off-time block, or <c>null</c> if not found.</summary>
    Task<OffTimeInfo?> GetOffTimeByIdAsync(Guid siteId, Guid offTimeId, CancellationToken ct = default);
    /// <summary>Creates an off-time block.</summary>
    Task<OffTimeInfo> CreateOffTimeAsync(Guid siteId, CreateOffTimeRequest request, CancellationToken ct = default);
    /// <summary>Updates an off-time block. Returns <c>null</c> if not found.</summary>
    Task<OffTimeInfo?> UpdateOffTimeAsync(Guid siteId, Guid offTimeId, UpdateOffTimeRequest request, CancellationToken ct = default);
    /// <summary>Deletes an off-time block. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteOffTimeAsync(Guid siteId, Guid offTimeId, CancellationToken ct = default);
    /// <summary>Re-evaluates start/end times for all scheduled requests on a site after settings change.</summary>
    Task RecalculateScheduledRequestsAsync(Guid siteId, CancellationToken ct = default);
    /// <summary>Applies scheduling rules to a create request, returning a potentially adjusted copy.</summary>
    Task<CreateRequestRequest> ApplySchedulingToCreateAsync(CreateRequestRequest request, CancellationToken ct = default);
    /// <summary>Applies scheduling rules to an update request.</summary>
    Task<UpdateRequestRequest> ApplySchedulingToUpdateAsync(Guid requestId, UpdateRequestRequest request, CancellationToken ct = default);
    /// <summary>Applies scheduling rules to a schedule-only patch request.</summary>
    Task<ScheduleRequestRequest> ApplySchedulingToScheduleAsync(Guid requestId, ScheduleRequestRequest request, CancellationToken ct = default);
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

    public Task<SchedulingSettingsInfo?> GetSettingsAsync(Guid siteId, CancellationToken ct = default)
        => _schedulingRepository.GetSettingsAsync(siteId, ct);

    public Task<SchedulingSettingsInfo> UpsertSettingsAsync(Guid siteId, UpsertSchedulingSettingsRequest request, CancellationToken ct = default)
        => _schedulingRepository.UpsertSettingsAsync(siteId, request, ct);

    public Task<bool> DeleteSettingsAsync(Guid siteId, CancellationToken ct = default)
        => _schedulingRepository.DeleteSettingsAsync(siteId, ct);

    public Task<List<OffTimeInfo>> GetOffTimesAsync(Guid siteId, CancellationToken ct = default)
        => _schedulingRepository.GetOffTimesAsync(siteId, ct);

    public Task<OffTimeInfo?> GetOffTimeByIdAsync(Guid siteId, Guid offTimeId, CancellationToken ct = default)
        => _schedulingRepository.GetOffTimeByIdAsync(siteId, offTimeId, ct);

    public Task<OffTimeInfo> CreateOffTimeAsync(Guid siteId, CreateOffTimeRequest request, CancellationToken ct = default)
        => _schedulingRepository.CreateOffTimeAsync(siteId, request, ct);

    public Task<OffTimeInfo?> UpdateOffTimeAsync(Guid siteId, Guid offTimeId, UpdateOffTimeRequest request, CancellationToken ct = default)
        => _schedulingRepository.UpdateOffTimeAsync(siteId, offTimeId, request, ct);

    public Task<bool> DeleteOffTimeAsync(Guid siteId, Guid offTimeId, CancellationToken ct = default)
        => _schedulingRepository.DeleteOffTimeAsync(siteId, offTimeId, ct);

    public async Task RecalculateScheduledRequestsAsync(Guid siteId, CancellationToken ct = default)
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
                    ResourceId = request.GetSpaceResourceId(),
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

    public async Task<CreateRequestRequest> ApplySchedulingToCreateAsync(CreateRequestRequest request, CancellationToken ct = default)
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
        Guid requestId, ScheduleRequestRequest request, CancellationToken ct = default)
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
        Guid resourceId, CancellationToken ct = default)
    {
        var siteId = await _schedulingRepository.GetSiteIdForResourceAsync(resourceId);
        if (siteId == null) return null;

        var settings = await _schedulingRepository.GetSettingsAsync(siteId.Value)
            ?? SchedulingSettingsInfo.Default(siteId.Value);

        var offTimes = await _schedulingRepository.GetOffTimesAsync(siteId.Value);
        return (settings, offTimes);
    }

    private async Task<SchedulingEngine.ScheduleResult?> ComputeScheduledTimesAsync(
        Guid resourceId, DateTime startTs, int durationValue, DurationUnit durationUnit, CancellationToken ct = default)
    {
        var ctx = await LoadSchedulingContextAsync(resourceId);
        if (ctx == null) return null;

        var durationMinutes = SchedulingEngine.DurationToMinutes(durationValue, durationUnit);
        return SchedulingEngine.CalculateSchedule(
            startTs, durationMinutes, true, ctx.Value.Settings, ctx.Value.OffTimes);
    }
}
