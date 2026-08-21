using System.Text.Json;
using Api.Models;

namespace Api.Services.Ai;

/// <summary>
/// One thing the assistant can do. Every implementation is read-only and runs in-process
/// under the caller's own workspace connection and role, so a tool call can never reach
/// data the caller could not already open in the UI.
/// </summary>
public interface IAiTool
{
    AiToolDefinition Definition { get; }

    /// <summary>Executes the tool and returns its result as text for the model to read.</summary>
    Task<string> ExecuteAsync(JsonElement input, CancellationToken ct);
}

/// <summary>
/// Tools whose call ends the turn instead of running anything. The model uses one to
/// propose a change; the loop stops, the user sees the proposal with concrete before and
/// after values, and the change only happens if they confirm it — through the ordinary
/// write endpoint, under their own session. The assistant never writes.
/// </summary>
public static class AiProposalTools
{
    public const string ProposeUpdateRequest = "propose_update_request";
    public const string ProposeAutoSchedule = "propose_auto_schedule";

    public static bool IsProposal(string toolName) =>
        toolName is ProposeUpdateRequest or ProposeAutoSchedule;

    public static IReadOnlyList<AiToolDefinition> Definitions { get; } =
    [
        new AiToolDefinition
        {
            Name = ProposeUpdateRequest,
            Description =
                "Propose a change to one request — a new time window, different resources, or a different site. " +
                "Call this when you have identified a concrete fix and can state every field that should change. " +
                "This does NOT apply the change: the person reviews it and decides. Say so when you call it.",
            InputSchemaJson = """
            {
              "type": "object",
              "properties": {
                "requestId": { "type": "string", "description": "The request to change." },
                "changes": {
                  "type": "object",
                  "description": "Only the fields that should change.",
                  "properties": {
                    "startTs": { "type": "string", "description": "New start, ISO 8601 UTC." },
                    "endTs": { "type": "string", "description": "New end, ISO 8601 UTC." },
                    "resourceIds": { "type": "array", "items": { "type": "string" }, "description": "Resources to assign instead of the current ones." },
                    "siteId": { "type": "string", "description": "New site." }
                  }
                },
                "rationale": { "type": "string", "description": "One or two sentences on why this fixes the problem." }
              },
              "required": ["requestId", "changes", "rationale"]
            }
            """,
        },
        new AiToolDefinition
        {
            Name = ProposeAutoSchedule,
            Description =
                "Propose running auto-scheduling for specific requests, when placing them is better left to the solver " +
                "than to a hand-picked slot. This does NOT run it: the person reviews the preview and decides.",
            InputSchemaJson = """
            {
              "type": "object",
              "properties": {
                "requestIds": { "type": "array", "items": { "type": "string" }, "description": "The requests to schedule." },
                "rationale": { "type": "string", "description": "One or two sentences on why the solver is the right tool here." }
              },
              "required": ["requestIds", "rationale"]
            }
            """,
        },
    ];
}

/// <summary>Reads the conflicts the workspace currently has.</summary>
public sealed class GetConflictsTool(IConflictService conflicts, IRequestService requests) : IAiTool
{
    public AiToolDefinition Definition { get; } = new()
    {
        Name = "get_conflicts",
        Description =
            "List scheduling conflicts in this workspace: overlaps, capacity and load problems, capability " +
            "mismatches, and placements outside their allowed window. Call this whenever the person asks what " +
            "is wrong with the plan, or before proposing a fix, so the advice matches the current state.",
        InputSchemaJson = """
        {
          "type": "object",
          "properties": {
            "requestId": { "type": "string", "description": "Limit to one request." },
            "limit": { "type": "integer", "description": "Maximum requests to report. Default 25." }
          }
        }
        """,
    };

    public async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        var limit = AiToolInput.Int(input, "limit") ?? 25;
        var requestFilter = AiToolInput.Guid(input, "requestId");

        var all = await conflicts.GetAllAsync(ct: ct);
        var scoped = requestFilter is { } id
            ? all.Where(c => c.RequestId == id).ToList()
            : all;

        if (scoped.Count == 0) return "No conflicts. Every scheduled request currently meets its requirements.";

        var trimmed = scoped.Take(limit).ToList();
        var names = (await requests.GetByIdsAsync(trimmed.Select(c => c.RequestId).ToList(), includeRequirements: false, ct))
            .ToDictionary(r => r.Id, r => r.Name);

        // Compact projection: enough for the model to reason and cite, no more. Full
        // entities would cost tokens and push workspace data to the provider needlessly.
        var payload = trimmed.Select(c => new
        {
            requestId = c.RequestId,
            requestName = names.GetValueOrDefault(c.RequestId, "(unknown)"),
            conflicts = c.Conflicts.Select(x => new
            {
                x.Kind,
                x.Severity,
                x.Message,
                peerRequestId = x.PeerRequestId,
                resourceId = x.ResourceId,
            }),
        });

        var truncated = scoped.Count > limit ? $"\n\n({scoped.Count - limit} more requests also have conflicts.)" : "";
        return AiToolInput.Json(payload) + truncated;
    }
}

/// <summary>Lists requests, optionally narrowed by name or scheduled state.</summary>
public sealed class GetRequestsTool(IRequestService requests) : IAiTool
{
    public AiToolDefinition Definition { get; } = new()
    {
        Name = "get_requests",
        Description =
            "List this workspace's requests — the work to be scheduled. Use it to find a request the person " +
            "named, or to see what is still unscheduled. Returns a summary per request; call get_request for detail.",
        InputSchemaJson = """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Case-insensitive match on the request name." },
            "scheduled": { "type": "boolean", "description": "True for scheduled only, false for unscheduled only." },
            "limit": { "type": "integer", "description": "Maximum to return. Default 25." }
          }
        }
        """,
    };

    public async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        var limit = AiToolInput.Int(input, "limit") ?? 25;
        var query = AiToolInput.String(input, "query");
        var scheduled = AiToolInput.Bool(input, "scheduled");

        IEnumerable<RequestInfo> all = await requests.GetAllAsync(includeRequirements: false, ct);

        if (!string.IsNullOrWhiteSpace(query))
            all = all.Where(r => r.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (scheduled is { } wantScheduled)
            all = all.Where(r => r.IsScheduled == wantScheduled);

        var page = all.Take(limit).Select(r => new
        {
            id = r.Id,
            name = r.Name,
            status = r.Status.ToString(),
            scheduled = r.IsScheduled,
            startTs = r.StartTs,
            endTs = r.EndTs,
            siteId = r.SiteId,
            needs = r.TargetResourceTypeKeys,
        }).ToList();

        return page.Count == 0 ? "No requests match." : AiToolInput.Json(page);
    }
}

/// <summary>Full detail for one request, including its assignments and requirements.</summary>
public sealed class GetRequestTool(IRequestService requests) : IAiTool
{
    public AiToolDefinition Definition { get; } = new()
    {
        Name = "get_request",
        Description =
            "Read one request in full: its time window, scheduling constraints, assigned resources, and " +
            "requirements. Call this before proposing any change, so the proposal is based on current values.",
        InputSchemaJson = """
        {
          "type": "object",
          "properties": {
            "requestId": { "type": "string", "description": "The request to read." }
          },
          "required": ["requestId"]
        }
        """,
    };

    public async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        if (AiToolInput.Guid(input, "requestId") is not { } id)
            return "requestId is required and must be a UUID.";

        var request = await requests.GetByIdAsync(id, includeRequirements: true, ct);
        if (request is null) return "No request with that id exists in this workspace.";

        return AiToolInput.Json(new
        {
            id = request.Id,
            name = request.Name,
            status = request.Status.ToString(),
            scheduled = request.IsScheduled,
            startTs = request.StartTs,
            endTs = request.EndTs,
            earliestStartTs = request.EarliestStartTs,
            latestEndTs = request.LatestEndTs,
            minimalDuration = $"{request.MinimalDurationValue} {request.MinimalDurationUnit}",
            siteId = request.SiteId,
            needs = request.TargetResourceTypeKeys,
            assignments = request.Assignments.Select(a => new
            {
                a.ResourceId,
                a.ResourceTypeKey,
                a.StartUtc,
                a.EndUtc,
                a.AllocationPercent,
            }),
            requirements = request.Requirements?.Select(r => new { r.CriterionId, r.Operator, r.Value }),
        });
    }
}

/// <summary>Small helpers so each tool reads its input the same way.</summary>
internal static class AiToolInput
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Json(object value) => JsonSerializer.Serialize(value, Options);

    public static string? String(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object
        && input.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    public static int? Int(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object
        && input.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt32(out var i)
            ? i
            : null;

    public static bool? Bool(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object
        && input.TryGetProperty(name, out var v)
        && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;

    public static Guid? Guid(JsonElement input, string name) =>
        System.Guid.TryParse(String(input, name), out var id) ? id : null;
}
