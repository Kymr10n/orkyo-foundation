namespace Api.Models;

/// <summary>Canonical response body for suspended tenant access attempts.</summary>
public record TenantSuspendedResponse
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required string TenantStatus { get; init; }
    public required string Reason { get; init; }
    public required bool SelfServiceAllowed { get; init; }
}