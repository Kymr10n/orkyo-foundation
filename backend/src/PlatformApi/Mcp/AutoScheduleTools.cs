using System.ComponentModel;
using Api.Helpers;
using Api.Models;
using Api.Security;
using Api.Services.AutoSchedule;
using FluentValidation;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Api.PlatformApi.Mcp;

/// <summary>
/// Auto-scheduling: the one place an agent can place many requests in a single call.
///
/// The safety model is the product's own, not something invented for MCP. Preview solves and
/// returns a plan plus a fingerprint; apply re-solves and refuses if the fingerprint no longer
/// matches, so a plan cannot be committed against a schedule that moved underneath.
///
/// <b>Why the fingerprint is mandatory here but optional over HTTP.</b> On the web a human sat in
/// front of the preview dialog and pressed Apply — the confirmation happened out of band. An
/// autonomous caller has no such moment, and our stateless transport means the server cannot ask
/// for one either (MCP elicitation needs a session). The fingerprint is therefore the only artifact
/// proving the caller saw the plan it is committing, so it is a required parameter: a model cannot
/// make apply its opening move.
/// </summary>
[McpServerToolType]
public sealed class AutoScheduleTools
{
    private readonly IAutoScheduleService _autoSchedule;
    private readonly IValidator<AutoSchedulePreviewRequest> _previewValidator;
    private readonly IValidator<AutoScheduleApplyRequest> _applyValidator;
    private readonly McpSolveThrottle _throttle;
    private readonly IAuthorizationContext _authorization;
    private readonly ICurrentTenant _tenant;

    public AutoScheduleTools(
        IAutoScheduleService autoSchedule,
        IValidator<AutoSchedulePreviewRequest> previewValidator,
        IValidator<AutoScheduleApplyRequest> applyValidator,
        McpSolveThrottle throttle,
        IAuthorizationContext authorization,
        ICurrentTenant tenant)
    {
        _autoSchedule = autoSchedule;
        _previewValidator = previewValidator;
        _applyValidator = applyValidator;
        _throttle = throttle;
        _authorization = authorization;
        _tenant = tenant;
    }

    // Read scope, matching the HTTP endpoint exactly: /preview carries .AllowMemberWrite() because
    // it persists nothing, so a Viewer may run one. Making MCP stricter than the UI would also
    // break the useful case of a read-only agent drafting a plan for a human to apply.
    [McpServerTool(Name = "auto_schedule_preview", Title = "Preview an auto-schedule",
        ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Solve the schedule for a site and horizon and return the proposed placements "
        + "WITHOUT saving anything. Read 'unscheduled' to see what could not be placed and why. To "
        + "commit the plan, pass the returned applyArguments straight to auto_schedule_apply.")]
    public async Task<AutoSchedulePreviewResult> PreviewAsync(
        [Description("Site to schedule. Required — get ids from list_sites.")] Guid siteId,
        [Description("First day of the horizon, YYYY-MM-DD.")] DateOnly horizonStart,
        [Description("Last day of the horizon, YYYY-MM-DD.")] DateOnly horizonEnd,
        [Description("Limit the solve to these requests. Omit to consider the site's whole backlog.")]
        IReadOnlyCollection<Guid>? requestIds = null,
        [Description("Which resource type to fill, e.g. 'machine'. One run fills one type. "
            + "Omit for the tenant's placeable default.")]
        string? resourceTypeKey = null,
        [Description("Honour the tenant's scheduling settings (working days, hours). Default true.")]
        bool respectSchedulingSettings = true,
        CancellationToken ct = default)
    {
        var request = new AutoSchedulePreviewRequest(
            siteId, horizonStart, horizonEnd, requestIds, respectSchedulingSettings, resourceTypeKey);
        await McpToolGuards.EnsureValidAsync(_previewValidator, request, ct);

        using var _ = await _throttle.AcquireAsync(_tenant.TenantId, ct);

        AutoSchedulePreviewResponse plan;
        try
        {
            plan = await _autoSchedule.PreviewAsync(request, ct);
        }
        catch (FeatureNotAvailableException ex)
        {
            throw new McpException(NotAvailable(ex));
        }
        catch (ArgumentException ex)
        {
            // The service raises this to say exactly what the caller must supply — most often
            // "several placeable resource types exist; specify resourceTypeKey", complete with the
            // valid keys. Letting it escape as an unhandled exception would replace the one message
            // that tells the agent how to succeed with a generic failure it can only retry blindly.
            throw new McpException(ex.Message);
        }

        // Echo the caller's own arguments back beside the fingerprint. ApplyAsync re-solves from
        // whatever it is given, so a fingerprint paired with a drifted horizon or a different
        // resource type produces a different solve and a stale-plan refusal the agent would read
        // as "the data changed" when it actually mistyped a parameter. Echoing removes that class
        // of failure. The type is echoed as supplied, not as resolved — re-resolution is
        // deterministic against the same tenant state, and the fingerprint catches it if it is not.
        var applyArguments = new AutoScheduleApplyArguments(
            siteId, horizonStart, horizonEnd, requestIds, resourceTypeKey,
            respectSchedulingSettings, plan.Fingerprint);

        return new AutoSchedulePreviewResult(plan, applyArguments);
    }

    // Destructive because it overwrites existing placements across the horizon; idempotent because
    // re-applying the same fingerprinted plan reaches the same end state.
    [McpServerTool(Name = "auto_schedule_apply", Title = "Apply an auto-schedule",
        Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Commit a plan produced by auto_schedule_preview. Pass the preview's "
        + "applyArguments unchanged, including previewFingerprint — the call is refused if the "
        + "schedule changed since the preview, and nothing is written. Requires 'schedule:write'.")]
    public async Task<AutoScheduleApplyResponse> ApplyAsync(
        [Description("Site to schedule, exactly as passed to the preview.")] Guid siteId,
        [Description("First day of the horizon, exactly as passed to the preview.")] DateOnly horizonStart,
        [Description("Last day of the horizon, exactly as passed to the preview.")] DateOnly horizonEnd,
        [Description("The fingerprint returned by auto_schedule_preview. Required: it is the proof "
            + "that this plan was seen before it was committed.")]
        string previewFingerprint,
        [Description("The same request ids passed to the preview, if any.")]
        IReadOnlyCollection<Guid>? requestIds = null,
        [Description("The same resource type key passed to the preview, if any.")]
        string? resourceTypeKey = null,
        [Description("The same value passed to the preview. Default true.")]
        bool respectSchedulingSettings = true,
        CancellationToken ct = default)
    {
        McpToolGuards.RequireWrite(_authorization, "auto_schedule_apply");

        var request = new AutoScheduleApplyRequest(
            siteId, horizonStart, horizonEnd, requestIds, respectSchedulingSettings,
            previewFingerprint, resourceTypeKey);
        await McpToolGuards.EnsureValidAsync(_applyValidator, request, ct);

        using var _ = await _throttle.AcquireAsync(_tenant.TenantId, ct);

        try
        {
            return await _autoSchedule.ApplyAsync(request, ct);
        }
        catch (ConflictException)
        {
            // The fingerprint check runs before any write, so "nothing was applied" is literally
            // true. Say so, and name the single correct next step — re-previewing, not retrying.
            throw new McpException(
                "The schedule changed since the preview was computed, so nothing was applied. "
                + "Call auto_schedule_preview again and apply the fingerprint it returns.");
        }
        catch (FeatureNotAvailableException ex)
        {
            throw new McpException(NotAvailable(ex));
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    /// <summary>
    /// Auto-scheduling is gated twice — by the edition's plan entitlement and by a tenant setting
    /// an administrator controls. Flattening both into one "not on your plan" message sends an
    /// admin whose only problem is a switch in Settings to a sales page instead, so the exception's
    /// own reason is passed through rather than replaced.
    /// </summary>
    private static string NotAvailable(FeatureNotAvailableException ex) =>
        $"Auto-scheduling is unavailable. {ex.Message}";
}

/// <summary>
/// A preview plan together with the exact arguments that commit it. Composed around
/// <see cref="AutoSchedulePreviewResponse"/> rather than restating its members, so a field added to
/// the solver's response reaches an agent without a second edit here.
/// </summary>
public sealed record AutoSchedulePreviewResult(
    AutoSchedulePreviewResponse Plan,
    AutoScheduleApplyArguments ApplyArguments);

/// <summary>The argument list for <c>auto_schedule_apply</c>, ready to pass back unchanged.</summary>
public sealed record AutoScheduleApplyArguments(
    Guid SiteId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    IReadOnlyCollection<Guid>? RequestIds,
    string? ResourceTypeKey,
    bool RespectSchedulingSettings,
    string PreviewFingerprint);
