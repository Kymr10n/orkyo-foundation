using System.Text.Json;
using Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orkyo.Shared;

namespace Api.Security.Challenge;

public sealed class CloudflareTurnstileProvider : IChallengeProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _secretKey;
    private readonly ILogger<CloudflareTurnstileProvider> _logger;

    private const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public CloudflareTurnstileProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<CloudflareTurnstileProvider> logger)
    {
        _httpClient = httpClient;
        _secretKey = configuration.GetRequired(ConfigKeys.TurnstileSecretKey);
        _logger = logger;
    }

    public async Task<ChallengeVerificationResult> VerifyAsync(string token, string clientIp, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_secretKey))
        {
            _logger.LogWarning("Turnstile secret key not configured, allowing request");
            return new ChallengeVerificationResult(true);
        }

        try
        {
            var payload = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = _secretKey,
                ["response"] = token,
                ["remoteip"] = clientIp
            });

            var response = await _httpClient.PostAsync(VerifyUrl, payload, ct);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var success = doc.RootElement.GetProperty("success").GetBoolean();

            if (!success)
            {
                var errorCodes = doc.RootElement.TryGetProperty("error-codes", out var codes)
                    ? codes.EnumerateArray().Select(e => e.GetString()).ToArray()
                    : Array.Empty<string?>();
                _logger.LogWarning("Turnstile verification failed: {Errors}", string.Join(", ", errorCodes));
                return new ChallengeVerificationResult(false, string.Join(", ", errorCodes));
            }

            return new ChallengeVerificationResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Turnstile verification request failed");
            return new ChallengeVerificationResult(true, "verification_unavailable");
        }
    }
}
