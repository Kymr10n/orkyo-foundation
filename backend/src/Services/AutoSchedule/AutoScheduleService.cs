using Api.Helpers;
using Api.Models;
using Api.Repositories;

namespace Api.Services.AutoSchedule;

public sealed class AutoScheduleService : IAutoScheduleService
{
    private const int MaxHorizonDays = 365;

    private readonly SchedulingProblemBuilder _problemBuilder;
    private readonly SchedulingFeasibilityAnalyzer _feasibilityAnalyzer;
    private readonly IEnumerable<ISchedulingSolver> _solvers;
    private readonly IRequestRepository _requestRepository;
    private readonly TenantContext _tenantContext;
    private readonly ITenantSettingsService _settingsService;
    private readonly ILogger<AutoScheduleService> _logger;

    public AutoScheduleService(
        SchedulingProblemBuilder problemBuilder,
        SchedulingFeasibilityAnalyzer feasibilityAnalyzer,
        IEnumerable<ISchedulingSolver> solvers,
        IRequestRepository requestRepository,
        TenantContext tenantContext,
        ITenantSettingsService settingsService,
        ILogger<AutoScheduleService> logger)
    {
        _problemBuilder = problemBuilder;
        _feasibilityAnalyzer = feasibilityAnalyzer;
        _solvers = solvers;
        _requestRepository = requestRepository;
        _tenantContext = tenantContext;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<AutoSchedulePreviewResponse> PreviewAsync(
        AutoSchedulePreviewRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureAutoScheduleAvailableAsync();
        Validate(request.HorizonStart, request.HorizonEnd);

        var problem = await _problemBuilder.BuildAsync(request, cancellationToken);
        var analyzed = _feasibilityAnalyzer.Analyze(problem);
        var solution = await SolveWithFallbackAsync(analyzed, cancellationToken);

        var requestNames = problem.Requests.ToDictionary(r => r.RequestId, r => r.DisplayName);
        var spaceNames = problem.Spaces.ToDictionary(s => s.SpaceId, s => s.DisplayName);

        return new AutoSchedulePreviewResponse(
            solution.SolverUsed,
            solution.Status,
            solution.ToScore(),
            solution.Assignments
                .Select(x => new ProposedAssignmentDto(
                    x.RequestId, requestNames.GetValueOrDefault(x.RequestId, "Unknown"),
                    x.SpaceId, spaceNames.GetValueOrDefault(x.SpaceId, "Unknown"),
                    x.Start, x.End, x.DurationDays))
                .ToList(),
            solution.Unscheduled
                .Select(x => new UnscheduledRequestDto(
                    x.RequestId, requestNames.GetValueOrDefault(x.RequestId, "Unknown"),
                    x.ReasonCodes))
                .ToList(),
            solution.Diagnostics,
            solution.ComputeFingerprint());
    }

    public async Task<AutoScheduleApplyResponse> ApplyAsync(
        AutoScheduleApplyRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureAutoScheduleAvailableAsync();
        Validate(request.HorizonStart, request.HorizonEnd);

        var preview = await PreviewAsync(
            new AutoSchedulePreviewRequest(
                request.SiteId, request.HorizonStart, request.HorizonEnd,
                request.Mode, request.RequestIds, request.RespectSchedulingSettings),
            cancellationToken);

        if (!string.IsNullOrEmpty(request.PreviewFingerprint) &&
            preview.Fingerprint != request.PreviewFingerprint)
        {
            throw new InvalidOperationException(
                "The scheduling data has changed since the preview was generated. " +
                "Please re-run the preview to get an up-to-date proposal.");
        }

        if (preview.Assignments.Count == 0)
            return new AutoScheduleApplyResponse(CreatedAssignments: 0, UnscheduledCount: preview.Unscheduled.Count);

        var updates = preview.Assignments
            .Select(a => (
                a.RequestId,
                new ScheduleRequestRequest
                {
                    SpaceId = a.SpaceId,
                    StartTs = a.Start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    EndTs = a.End.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                }))
            .ToList();

        var created = await _requestRepository.BatchUpdateSchedulesAsync(updates);

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
        if (_tenantContext.Tier < ServiceTier.Professional)
            throw new FeatureNotAvailableException("Auto-Schedule", _tenantContext.Tier.ToString(), ServiceTier.Professional.ToString());

        var settings = await _settingsService.GetSettingsAsync();
        if (!settings.AutoSchedule_Enabled)
            throw new FeatureNotAvailableException(
                "Auto-Schedule",
                "Auto-scheduling is not enabled. A tenant administrator can enable it in Settings > Configuration.");
    }

    private static void Validate(DateOnly start, DateOnly end)
    {
        if (end <= start)
            throw new ArgumentException("HorizonEnd must be after HorizonStart.");
        if (end.DayNumber - start.DayNumber > MaxHorizonDays)
            throw new ArgumentException($"Horizon cannot exceed {MaxHorizonDays} days.");
    }
}
