namespace Api.Security.Challenge;

public record ChallengeVerificationResult(bool Success, string? ErrorCode = null);

public interface IChallengeProvider
{
    Task<ChallengeVerificationResult> VerifyAsync(string token, string clientIp, CancellationToken ct = default);
}
