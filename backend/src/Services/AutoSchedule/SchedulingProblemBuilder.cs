using Api.Models;
using Api.Repositories;

namespace Api.Services.AutoSchedule;

public class SchedulingProblemBuilder
{
    private readonly IRequestRepository _requestRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly ISpaceCapabilityRepository _capabilityRepository;
    private readonly ISchedulingRepository _schedulingRepository;

    public SchedulingProblemBuilder(
        IRequestRepository requestRepository,
        ISpaceRepository spaceRepository,
        ISpaceCapabilityRepository capabilityRepository,
        ISchedulingRepository schedulingRepository)
    {
        _requestRepository = requestRepository;
        _spaceRepository = spaceRepository;
        _capabilityRepository = capabilityRepository;
        _schedulingRepository = schedulingRepository;
    }

    public virtual async Task<SchedulingProblem> BuildAsync(
        AutoSchedulePreviewRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await _schedulingRepository.GetSettingsAsync(request.SiteId);
        var offTimes = await _schedulingRepository.GetOffTimesAsync(request.SiteId);
        var allRequests = await _requestRepository.GetAllAsync(includeRequirements: true);

        var eligibleRequests = allRequests
            .Where(r => !r.IsScheduled)
            .Where(r => r.PlanningMode == PlanningMode.Leaf)
            .Where(r => r.Status is RequestStatus.Planned or RequestStatus.InProgress)
            .Where(r => r.MinimalDurationValue > 0);

        if (request.RequestIds is { Count: > 0 })
        {
            var requestIdSet = request.RequestIds.ToHashSet();
            eligibleRequests = eligibleRequests.Where(r => requestIdSet.Contains(r.Id));
        }

        var spaces = await _spaceRepository.GetAllAsync(request.SiteId);
        var spaceNodes = new List<SpaceNode>();
        foreach (var space in spaces)
        {
            var capabilities = await _capabilityRepository.GetAllAsync(request.SiteId, space.Id);
            spaceNodes.Add(new SpaceNode(space.Id, space.Name, capabilities.Select(c => c.CriterionId).ToHashSet()));
        }

        var requestNodes = new List<RequestNode>();
        foreach (var r in eligibleRequests)
        {
            var durationDays = DurationToDays(r.MinimalDurationValue, r.MinimalDurationUnit, settings);
            if (durationDays <= 0) continue;

            requestNodes.Add(new RequestNode(
                r.Id,
                r.Name,
                r.EarliestStartTs.HasValue ? DateOnly.FromDateTime(r.EarliestStartTs.Value) : (DateOnly?)null,
                r.LatestEndTs.HasValue ? DateOnly.FromDateTime(r.LatestEndTs.Value) : (DateOnly?)null,
                durationDays,
                Priority: (int)r.Status,
                r.SchedulingSettingsApply,
                r.Requirements?.Select(req => req.CriterionId).ToHashSet() ?? new HashSet<Guid>()));
        }

        var fixedAssignments = allRequests
            .Where(r => r.IsScheduled)
            .Select(r => new FixedOccupancy(
                r.Id,
                r.SpaceId!.Value,
                DateOnly.FromDateTime(r.StartTs!.Value),
                DateOnly.FromDateTime(r.EndTs!.Value)))
            .ToList();

        return new SchedulingProblem(
            request.SiteId, request.HorizonStart, request.HorizonEnd,
            requestNodes, spaceNodes, fixedAssignments,
            settings, offTimes, request.Mode);
    }

    private static int DurationToDays(int value, DurationUnit unit, SchedulingSettingsInfo? settings)
    {
        var minutesPerDay = 24 * 60;
        if (settings is { WorkingHoursEnabled: true })
        {
            minutesPerDay = (int)(settings.WorkingDayEnd - settings.WorkingDayStart).TotalMinutes;
            if (minutesPerDay <= 0) minutesPerDay = 8 * 60;
        }
        var totalMinutes = SchedulingEngine.DurationToMinutes(value, unit);
        return Math.Max(1, (int)Math.Ceiling((double)totalMinutes / minutesPerDay));
    }
}
