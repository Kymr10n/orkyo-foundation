using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Api.Security.Features;

namespace Api.Services.AutoSchedule;

public sealed class AutoScheduleService : IAutoScheduleService
{
    private const int MaxHorizonDays = 365;

    private readonly SchedulingProblemBuilder _problemBuilder;
    private readonly SchedulingFeasibilityAnalyzer _feasibilityAnalyzer;
    private readonly IEnumerable<ISchedulingSolver> _solvers;
    private readonly IRequestRepository _requestRepository;
    private readonly IResourceTypeRepository _resourceTypeRepository;
    private readonly IFeatureGate _featureGate;
    private readonly ITenantSettingsService _settingsService;
    private readonly ILogger<AutoScheduleService> _logger;

    public AutoScheduleService(
        SchedulingProblemBuilder problemBuilder,
        SchedulingFeasibilityAnalyzer feasibilityAnalyzer,
        IEnumerable<ISchedulingSolver> solvers,
        IRequestRepository requestRepository,
        IResourceTypeRepository resourceTypeRepository,
        IFeatureGate featureGate,
        ITenantSettingsService settingsService,
        ILogger<AutoScheduleService> logger)
    {
        _problemBuilder = problemBuilder;
        _feasibilityAnalyzer = feasibilityAnalyzer;
        _solvers = solvers;
        _requestRepository = requestRepository;
        _resourceTypeRepository = resourceTypeRepository;
        _featureGate = featureGate;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<AutoSchedulePreviewResponse> PreviewAsync(
        AutoSchedulePreviewRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureAutoScheduleAvailableAsync();
        Validate(request.HorizonStart, request.HorizonEnd);

        // Which types the run fills is resolved once, here, so the builder and the fingerprint
        // see the same set. Omitted means every type a request can target.
        request = request with { ResourceTypeKeys = await ResolveTargetTypesAsync(request.ResourceTypeKeys, cancellationToken) };

        var problem = await _problemBuilder.BuildAsync(request, cancellationToken);
        var analyzed = _feasibilityAnalyzer.Analyze(problem);
        var solution = await SolveWithFallbackAsync(analyzed, cancellationToken);

        var requestNames = problem.Requests.ToDictionary(r => r.RequestId, r => r.DisplayName);
        var resourceNames = problem.Resources.ToDictionary(s => s.ResourceId, s => s.DisplayName);

        return new AutoSchedulePreviewResponse(
            solution.SolverUsed,
            solution.Status,
            solution.ToScore(),
            solution.Assignments
                // Offsets become timestamps here and nowhere else. Start snaps forward at a
                // boundary and End backward, so a job ending at close of business is written
                // as ending then rather than at the next morning's opening.
                .Select(x => new ProposedAssignmentDto(
                    x.RequestId, requestNames.GetValueOrDefault(x.RequestId, "Unknown"),
                    x.Resources
                        .Select(r => new ProposedResourceDto(
                            r.TypeKey, r.ResourceId, resourceNames.GetValueOrDefault(r.ResourceId, "Unknown")))
                        .ToList(),
                    problem.Axis.StartAt(x.Start), problem.Axis.EndAt(x.End), x.DurationMinutes))
                .ToList(),
            solution.Unscheduled
                .Select(x => new UnscheduledRequestDto(
                    x.RequestId, requestNames.GetValueOrDefault(x.RequestId, "Unknown"),
                    x.ReasonCodes))
                // Requests the builder withheld never reached a solver, so they are absent from
                // its Unscheduled list. Reporting only what the solver saw would return fewer
                // requests than the caller selected, with nothing said about the difference.
                .Concat((problem.Withheld ?? [])
                    .Select(w => new UnscheduledRequestDto(
                        w.RequestId, w.DisplayName,
                        [SchedulingReasonCode.PredecessorUnscheduled])))
                .ToList(),
            solution.Diagnostics,
            solution.ComputeFingerprint(request.ResourceTypeKeys!, problem.Dependencies ?? [], problem.JoinConditions));
    }

    public async Task<AutoScheduleApplyResponse> ApplyAsync(
        AutoScheduleApplyRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureAutoScheduleAvailableAsync();
        Validate(request.HorizonStart, request.HorizonEnd);

        // ResourceTypeKeys must cross into the rebuilt preview. Drop them and apply would silently
        // re-solve for every type, so a preview of van assignments would be applied with rooms
        // too — and the fingerprint would not catch it, being computed from whatever this call solved.
        var preview = await PreviewAsync(
            new AutoSchedulePreviewRequest(
                request.SiteId, request.HorizonStart, request.HorizonEnd,
                request.RequestIds, request.RespectSchedulingSettings, request.ResourceTypeKeys),
            cancellationToken);

        if (!string.IsNullOrEmpty(request.PreviewFingerprint) &&
            preview.Fingerprint != request.PreviewFingerprint)
        {
            // ConflictException, not InvalidOperationException: the latter maps to no arm
            // in AppExceptionHandler, so a stale preview surfaced as a 500 and the dialog's
            // "close and re-run" branch — which keys on 409 — never showed.
            throw new ConflictException(
                "The scheduling data has changed since the preview was generated. " +
                "Please re-run the preview to get an up-to-date proposal.");
        }

        if (preview.Assignments.Count == 0)
            return new AutoScheduleApplyResponse(CreatedAssignments: 0, UnscheduledCount: preview.Unscheduled.Count);

        // Already the half-open timestamp window the preview showed.
        var placements = preview.Assignments
            .Select(a => new PlacementWrite(
                a.RequestId, a.Start, a.End, a.Resources.Select(r => r.ResourceId).ToList()))
            .ToList();

        var created = await _requestRepository.BatchApplyPlacementsAsync(placements, cancellationToken);

        _logger.LogInformation("Auto-schedule applied: {Count} assignments created for site {SiteId}",
            created, request.SiteId);

        return new AutoScheduleApplyResponse(CreatedAssignments: created, UnscheduledCount: preview.Unscheduled.Count);
    }

    private async Task<SchedulingSolution> SolveWithFallbackAsync(
        AnalyzedSchedulingProblem analyzed,
        CancellationToken cancellationToken)
    {
        var primarySolver = _solvers.OrderByDescending(x => x.Priority).First();
        var fallbackSolver = _solvers.Where(x => x.Kind == SolverKind.Greedy).OrderByDescending(x => x.Priority).First();

        try
        {
            var solution = await primarySolver.SolveAsync(analyzed, cancellationToken);
            if (primarySolver.Kind != SolverKind.Greedy &&
                solution.Status is SolverStatus.Infeasible or SolverStatus.Unknown)
            {
                _logger.LogWarning("Primary solver ({Kind}) returned {Status}, falling back to greedy",
                    primarySolver.Kind, solution.Status);
                return await fallbackSolver.SolveAsync(analyzed, cancellationToken);
            }
            return solution;
        }
        catch (Exception ex) when (primarySolver.Kind != SolverKind.Greedy)
        {
            _logger.LogError(ex, "Primary solver ({Kind}) failed, falling back to greedy", primarySolver.Kind);
            return await fallbackSolver.SolveAsync(analyzed, cancellationToken);
        }
    }

    private async Task EnsureAutoScheduleAvailableAsync()
    {
        // Commercial gating is delegated to the edition (SaaS: tier entitlement; Community: allow-all).
        await _featureGate.EnsureEnabledAsync(FeatureKeys.AutoSchedule);

        var settings = await _settingsService.GetSettingsAsync();
        if (!settings.AutoSchedule_Enabled)
            throw new FeatureNotAvailableException(
                "Auto-Schedule",
                "Auto-scheduling is not enabled. A tenant administrator can enable it in Settings > Configuration.");
    }

    /// <summary>
    /// The sorted, distinct set of type keys the run fills. Given keys must name active types;
    /// none given means every active type a request can target. People carry a directory
    /// profile and are attached on the request's own tab, many per request — not a slot.
    /// </summary>
    private async Task<IReadOnlyCollection<string>> ResolveTargetTypesAsync(
        IReadOnlyCollection<string>? requested, CancellationToken ct)
    {
        var active = (await _resourceTypeRepository.GetAllAsync(ct)).Where(t => t.IsActive).ToList();

        if (requested is { Count: > 0 })
        {
            var known = active.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
            var unknown = requested.Where(k => !known.Contains(k)).Distinct().Order().ToList();
            if (unknown.Count > 0)
                throw new ArgumentException(
                    $"Unknown or inactive resource type(s): {string.Join(", ", unknown)}.");
            return requested.Distinct().Order().ToList();
        }

        var keys = active.Where(t => !t.HasDirectoryProfile).Select(t => t.Key).Order().ToList();
        if (keys.Count == 0)
            throw new ArgumentException(
                "No schedulable resource types exist, so there is nothing to schedule onto.");
        return keys;
    }

    private static void Validate(DateOnly start, DateOnly end)
    {
        if (end <= start)
            throw new ArgumentException("HorizonEnd must be after HorizonStart.");
        if (end.DayNumber - start.DayNumber > MaxHorizonDays)
            throw new ArgumentException($"Horizon cannot exceed {MaxHorizonDays} days.");
    }
}
