using Api.Helpers;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Api.PlatformApi.Mcp;

/// <summary>
/// The one filter every tools/call passes through. It does the two jobs that belong to the
/// pipeline rather than to any single tool:
///
/// 1. <b>Attribution.</b> One log line per call — tool, actor (the token id, which the auth
///    handler put on <c>Identity.Name</c> and which doubles as the audit user id), arguments and
///    outcome. Scheduling has no domain audit trail, so for a non-human write-capable credential
///    this line is the non-repudiation story. Logging here rather than inside each tool means a
///    new tool cannot forget it.
/// 2. <b>Containment.</b> A domain exception (<see cref="DomainRefusal"/>) becomes an
///    <see cref="McpException"/> with its own message, as <c>AppExceptionHandler</c> makes it a
///    4xx for HTTP. Anything else must not hand the raw message to a third-party LLM client — an
///    <c>NpgsqlException</c> carries SQL text and column names. The real exception is logged; the
///    client gets a generic failure.
/// </summary>
public static class McpToolPipeline
{
    /// <summary>Registered via <c>WithRequestFilters(b =&gt; b.AddCallToolFilter(...))</c>.</summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> AuditAndContainErrors(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
        => async (context, cancellationToken) =>
        {
            var tool = context.Params?.Name ?? "(unknown)";
            var actor = context.User?.Identity?.Name ?? "(anonymous)";
            var logger = context.Services?.GetService<ILoggerFactory>()
                ?.CreateLogger("Api.PlatformApi.Mcp.McpToolPipeline");

            try
            {
                var result = await next(context, cancellationToken);
                logger?.LogInformation(
                    "MCP call: tool={Tool} actor={Actor} ok args={Arguments}",
                    tool, actor, Truncate(context.Params?.Arguments?.ToString()));
                return result;
            }
            catch (McpException ex)
            {
                // Deliberate refusals (scope, validation, stale fingerprint, throttle) — their
                // messages are written for the agent and pass through untouched.
                logger?.LogInformation(
                    "MCP call: tool={Tool} actor={Actor} refused: {Reason}", tool, actor, ex.Message);
                throw;
            }
            catch (OperationCanceledException)
            {
                throw; // The client went away; there is nobody to translate for.
            }
            catch (Exception ex) when (DomainRefusal(ex) is { } reason)
            {
                // A domain refusal (not found, conflict, bad argument, feature off, quota) is the
                // agent's to act on, exactly as AppExceptionHandler answers it with a 4xx for HTTP.
                logger?.LogInformation(
                    "MCP call: tool={Tool} actor={Actor} refused: {Reason}", tool, actor, reason);
                throw new McpException(reason);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex,
                    "MCP call: tool={Tool} actor={Actor} failed unexpectedly", tool, actor);
                throw new McpException(
                    $"The '{tool}' tool failed unexpectedly. The error is logged on the server; "
                    + "nothing about it is available to this client. Retrying the identical call "
                    + "is unlikely to help.");
            }
        };

    /// <summary>
    /// The exceptions whose message is written for the caller — exactly the ones
    /// <see cref="AppExceptionHandler"/> turns into a 4xx, so the two surfaces cannot drift.
    /// Guard-clause failures (<see cref="ArgumentNullException"/>,
    /// <see cref="ArgumentOutOfRangeException"/>) are programming errors that name internal
    /// parameters, so they stay a generic failure there and here.
    /// </summary>
    internal static string? DomainRefusal(Exception ex) => AppExceptionHandler.ClientErrorDetail(ex);

    /// <summary>Arguments are small tool inputs, but nothing stops a client sending a novel.</summary>
    private static string Truncate(string? arguments)
        => arguments is null ? "(none)"
            : arguments.Length <= 500 ? arguments
            : arguments[..500] + "…";
}
