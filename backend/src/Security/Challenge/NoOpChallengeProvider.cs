namespace Api.Security.Challenge;

public sealed class NoOpChallengeProvider : IChallengeProvider
{
    public Task<ChallengeVerificationResult> VerifyAsync(string token, string clientIp, CancellationToken ct = default)
        => Task.FromResult(new ChallengeVerificationResult(true));
}
