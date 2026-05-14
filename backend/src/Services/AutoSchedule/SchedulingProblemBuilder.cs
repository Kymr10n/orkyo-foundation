using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services.AutoSchedule;

public class SchedulingProblemBuilder
{
    private readonly IRequestRepository _requestRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IResourceCapabilityRepository _capabilityRepository;
    private readonly ISchedulingRepository _schedulingRepository;
    private readonly IResourceRepository _resourceRepository;

    public SchedulingProblemBuilder(
        IRequestRepository requestRepository,
        ISpaceRepository spaceRepository,
        IResourceCapabilityRepository capabilityRepository,
        ISchedulingRepository schedulingRepository,
        IResourceRepository resourceRepository)
    {
        _requestRepository = requestRepository;
        _spaceRepository = spaceRepository;
        _capabilityRepository = capabilityRepository;
        _schedulingRepository = schedulingRepository;
        _resourceRepository = resourceRepository;
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
            var capabilities = await _capabilityRepository.GetByResourceAsync(space.Id);
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
                r.PrimaryResourceId!.Value,
                DateOnly.FromDateTime(r.StartTs!.Value),
                DateOnly.FromDateTime(r.EndTs!.Value)))
            .ToList();

        // Load non-Space resources only when at least one request carries additional requirements.
        var needsAdditionalResources = requestNodes.Any(n =>
            n.AdditionalRequirements is { Count: > 0 });

        var additionalResources = needsAdditionalResources
            ? await LoadNonSpaceResourcesAsync()
            : (IReadOnlyList<ResourceNode>)[];

        return new SchedulingProblem(
            request.SiteId, request.HorizonStart, request.HorizonEnd,
            requestNodes, spaceNodes, fixedAssignments,
            settings, offTimes, request.Mode, additionalResources);
    }

    private async Task<IReadOnlyList<ResourceNode>> LoadNonSpaceResourcesAsync()
    {
        var all = await _resourceRepository.GetAllAsync(new ResourceListFilter { IsActive = true });
        var nonSpace = all.Where(r => r.ResourceTypeKey != ResourceTypeKeys.Space).ToList();

        var nodes = new List<ResourceNode>();
        foreach (var r in nonSpace)
        {
            var caps = await _capabilityRepository.GetByResourceAsync(r.Id);
            nodes.Add(new ResourceNode(
                r.Id,
                r.ResourceTypeId,
                r.AllocationMode,
                caps.Select(c => c.CriterionId).ToHashSet()));
        }
        return nodes;
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
