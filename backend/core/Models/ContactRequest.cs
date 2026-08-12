namespace Api.Models;

/// <summary>
/// Request model for the public contact form on marketing pages.
/// </summary>
public record ContactRequest : IChallengeProtectedRequest
{
    public required string Name { get; init; }
    public required string Email { get; init; }
    public string? Company { get; init; }
    public required string Subject { get; init; }
    public required string Message { get; init; }
    public string? ChallengeToken { get; init; }
}
