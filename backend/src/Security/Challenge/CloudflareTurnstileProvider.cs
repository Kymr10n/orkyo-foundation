using System.Text.Json;
using Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkyo.Shared;

namespace Api.Security.Challenge;

public sealed class CloudflareTurnstileProvider : IChallengeProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _secretKey;
    private readonly string _expectedHostname;
    private readonly bool _isDevelopment;
    private readonly ILogger<CloudflareTurnstileProvider> _logger;

    private const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    private const string VerificationUnavailable = "verification_unavailable";
    private const string HostnameMismatch = "hostname_mismatch";

    public CloudflareTurnstileProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<CloudflareTurnstileProvider> logger)
    {
        _httpClient = httpClient;
        _secretKey = configuration.GetRequired(ConfigKeys.TurnstileSecretKey);
        // Every widget renders on the app's own host (apex gateway, marketing pages), so a
        // token issued for any other hostname is not accepted.
        _expectedHostname = new Uri(configuration.GetRequired(ConfigKeys.AppBaseUrl), UriKind.Absolute).Host;
        _isDevelopment = environment.IsDevelopment();
        _logger = logger;
    }

    public async Task<ChallengeVerificationResult> VerifyAsync(string token, string clientIp, CancellationToken ct = default)
    {
        try
        {
            var payload = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = _secretKey,
                ["response"] = token,
                ["remoteip"] = clientIp
            });

            var response = await _httpClient.PostAsync(VerifyUrl, payload, ct);
            if (!response.IsSuccessStatusCode)
                return Unavailable($"siteverify returned HTTP {(int)response.StatusCode}");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("success", out var successElement) ||
                successElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return Unavailable("siteverify response has no boolean 'success' field");

            if (!successElement.GetBoolean())
            {
                var errorCodes = doc.RootElement.TryGetProperty("error-codes", out var codes)
                    ? codes.EnumerateArray().Select(e => e.GetString()).ToArray()
                    : Array.Empty<string?>();
                _logger.LogWarning("Turnstile verification failed: {Errors}", string.Join(", ", errorCodes));
                return new ChallengeVerificationResult(false, string.Join(", ", errorCodes));
            }

            var hostname = doc.RootElement.TryGetProperty("hostname", out var hostElement) &&
                           hostElement.ValueKind == JsonValueKind.String
                ? hostElement.GetString()
                : null;
            if (!string.Equals(hostname, _expectedHostname, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Turnstile token was issued for hostname {Hostname}, expected {ExpectedHostname}",
                    hostname, _expectedHostname);
                return new ChallengeVerificationResult(false, HostnameMismatch);
            }

            return new ChallengeVerificationResult(true);
        }
        catch (Exception ex)
        {
            return Unavailable(ex.GetType().Name);
        }
    }

    /// <summary>
    /// The verification service gave no usable answer. Only Development lets the request
    /// through; every other environment rejects it, and logs that as an Error because a real
    /// visitor was turned away.
    /// </summary>
    private ChallengeVerificationResult Unavailable(string reason)
    {
        _logger.Log(_isDevelopment ? LogLevel.Warning : LogLevel.Error,
            "Turnstile verification unavailable ({Reason}); request {Outcome}",
            reason, _isDevelopment ? "allowed (Development)" : "rejected");
        return new ChallengeVerificationResult(_isDevelopment, VerificationUnavailable);
    }
}
