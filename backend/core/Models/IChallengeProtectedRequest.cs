namespace Api.Models;

/// <summary>
/// A public anonymous request body that carries an optional bot-challenge token.
/// Endpoints opt in with <c>RequireChallengeVerification()</c>; the filter reads
/// the token from the first handler argument that implements this interface.
/// </summary>
public interface IChallengeProtectedRequest
{
    string? ChallengeToken { get; }
}
