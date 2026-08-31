using Api.Security;
using FluentValidation;
using ModelContextProtocol;

namespace Api.PlatformApi.Mcp;

/// <summary>
/// The two checks every MCP tool class needs, in one place so four classes cannot drift apart.
///
/// Both exist because MCP bypasses the two places the HTTP pipeline normally puts them: the
/// verb-aware write gate (MCP carries every call over one POST, so the verb says nothing), and
/// <c>EndpointHelpers.ExecuteAsync</c>, which is where endpoints run their FluentValidation
/// validator. Neither re-implements anything — the write gate reads the same
/// <see cref="IAuthorizationContext.CanEdit"/> property <c>RequireEditAccess</c> uses, and the
/// validator bridge runs the very validator object the endpoint would have run.
/// </summary>
public static class McpToolGuards
{
    /// <summary>
    /// Refuses a mutating tool to a read-only token. Throws <see cref="McpException"/> so the model
    /// receives a protocol error it can reason about, rather than a 500 that reads as a transport
    /// fault worth retrying.
    /// </summary>
    /// <remarks>
    /// The message must keep naming <c>schedule:write</c> literally — the integration suite asserts
    /// on that string as the proof that a read-only token was refused for the right reason.
    /// </remarks>
    public static void RequireWrite(IAuthorizationContext authorization, string tool)
    {
        if (!authorization.CanEdit)
            throw new McpException(
                $"The '{tool}' tool needs the 'schedule:write' scope. This token is read-only.");
    }

    /// <summary>
    /// Runs the same validator the HTTP endpoint runs for this request shape. Without it a tool
    /// would hand an unvalidated payload straight to a service that assumes the endpoint already
    /// checked it.
    /// </summary>
    public static async Task EnsureValidAsync<T>(
        IValidator<T> validator, T request, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(request, ct);
        if (result.IsValid) return;

        throw new McpException(
            string.Join(" ", result.Errors.Select(e => e.ErrorMessage)));
    }
}
