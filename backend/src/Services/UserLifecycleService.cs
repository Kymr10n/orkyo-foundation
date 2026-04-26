using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Orkyo.Shared;
using Orkyo.Shared.Keycloak;

namespace Api.Services;

/// <summary>
/// GDPR user account lifecycle — product-agnostic, consumed by both SaaS and Community workers.
///
/// Flow:
///   1. Users inactive 12+ months → warning email #1 with confirm-activity link.
///   2. No response in 14 days    → warning email #2.
///   3. No response in 14 days    → warning email #3 (final).
///   4. No response in 14 days    → account disabled in Keycloak (dormant), dormancy notice sent.
///   5. Dormant 90+ days          → purged from Keycloak and app data deleted.
///
/// State is stored in the <c>users</c> table (lifecycle_* columns).
/// Reset occurs on login (<c>/api/session/bootstrap</c>) or confirm-activity link.
///
/// The DB connection is created via <see cref="IDbConnectionFactory.CreateControlPlaneConnection"/>.
/// In Community, the factory maps this to the single deployment database.
/// </summary>
public sealed class UserLifecycleService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<UserLifecycleService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly KeycloakOptions _kc;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private static readonly SemaphoreSlim _emailThrottle = new(5, 5);
    private string? _kcAccessToken;
    private DateTime _kcTokenExpiry = DateTime.MinValue;

    private readonly Lazy<SmtpConfig> _smtpConfig;

    private string AppBaseUrl => _configuration[ConfigKeys.AppBaseUrl]
        ?? throw new InvalidOperationException("APP_BASE_URL not configured");

    public UserLifecycleService(
        IConfiguration configuration,
        ILogger<UserLifecycleService> logger,
        IHttpClientFactory httpClientFactory,
        IDbConnectionFactory connectionFactory,
        KeycloakOptions keycloakOptions)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _connectionFactory = connectionFactory;
        _kc = keycloakOptions;

        _smtpConfig = new Lazy<SmtpConfig>(() => new SmtpConfig(
            Host: configuration[ConfigKeys.SmtpHost]
                ?? throw new InvalidOperationException("SMTP_HOST not configured"),
            Port: int.TryParse(configuration[ConfigKeys.SmtpPort], out var p) ? p
                : throw new InvalidOperationException("SMTP_PORT is not configured or is not a valid integer"),
            UseSsl: bool.TryParse(configuration[ConfigKeys.SmtpUseSsl], out var s) ? s
                : throw new InvalidOperationException("SMTP_USE_SSL is not configured or is not 'true'/'false'"),
            Username: configuration[ConfigKeys.SmtpUsername],
            Password: configuration[ConfigKeys.SmtpPassword],
            FromEmail: configuration[ConfigKeys.SmtpFromEmail]
                ?? throw new InvalidOperationException("SMTP_FROM_EMAIL not configured"),
            FromName: configuration[ConfigKeys.SmtpFromName]
                ?? throw new InvalidOperationException("SMTP_FROM_NAME not configured")
        ));
    }

    private sealed record SmtpConfig(
        string Host, int Port, bool UseSsl,
        string? Username, string? Password,
        string FromEmail, string FromName);

    public async Task ProcessAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting user lifecycle run");

        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync(ct);

        await SendWarningsAsync(db, 0, ct);
        await SendWarningsAsync(db, 1, ct);
        await SendWarningsAsync(db, 2, ct);
        await DeactivatePersistentlyInactiveUsersAsync(db, ct);
        await PurgeDormantUsersAsync(db, ct);

        _logger.LogInformation("User lifecycle run complete");
    }

    private async Task SendWarningsAsync(Npgsql.NpgsqlConnection db, int currentWarningCount, CancellationToken ct)
    {
        var nextCount = currentWarningCount + 1;

        var cmd = currentWarningCount == 0
            ? new Npgsql.NpgsqlCommand($@"
                SELECT id, email, display_name, keycloak_id
                FROM users
                WHERE lifecycle_status IS NULL
                  AND status = 'active'
                  AND (
                    (last_login_at IS NOT NULL AND last_login_at < NOW() - INTERVAL '{LifecyclePolicyConstants.UserInactiveWarningSqlInterval}')
                    OR
                    (last_login_at IS NULL AND created_at < NOW() - INTERVAL '{LifecyclePolicyConstants.UserInactiveWarningSqlInterval}')
                  )", db)
            : new Npgsql.NpgsqlCommand($@"
                SELECT id, email, display_name, keycloak_id
                FROM users
                WHERE lifecycle_status = 'warned'
                  AND lifecycle_warning_count = @count
                  AND lifecycle_last_warned_at < NOW() - INTERVAL '{LifecyclePolicyConstants.UserWarningReminderSqlInterval}'", db);

        if (currentWarningCount > 0)
            cmd.Parameters.AddWithValue("count", currentWarningCount);

        var users = await ReadUsersAsync(cmd, ct);
        _logger.LogInformation("Warning phase #{NextCount}: {Count} user(s) to warn", nextCount, users.Count);

        foreach (var user in users)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var token = Guid.NewGuid().ToString();

                await using var tx = await db.BeginTransactionAsync(ct);
                await UpdateLifecycleAsync(db, user.Id, status: "warned", warningCount: nextCount,
                    lastWarnedAt: DateTime.UtcNow, dormantSince: null, confirmToken: token, ct);
                await tx.CommitAsync(ct);

                await SendLifecycleWarningEmailAsync(user.Email, user.DisplayName, token, warningNumber: nextCount);
                _logger.LogInformation("Warning #{NextCount} sent to user {UserId}", nextCount, user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process warning #{NextCount} for user {UserId}", nextCount, user.Id);
            }
        }
    }

    private async Task DeactivatePersistentlyInactiveUsersAsync(Npgsql.NpgsqlConnection db, CancellationToken ct)
    {
        var cmd = new Npgsql.NpgsqlCommand($@"
            SELECT id, email, display_name, keycloak_id
            FROM users
            WHERE lifecycle_status = 'warned'
              AND lifecycle_warning_count = 3
              AND lifecycle_last_warned_at < NOW() - INTERVAL '{LifecyclePolicyConstants.UserWarningReminderSqlInterval}'
        ", db);

        var users = await ReadUsersAsync(cmd, ct);
        _logger.LogInformation("Phase 4 (deactivate): {Count} user(s) to deactivate", users.Count);

        foreach (var user in users)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (!string.IsNullOrEmpty(user.KeycloakId))
                {
                    var (success, error) = await SetKeycloakUserEnabledAsync(user.KeycloakId, enabled: false);
                    if (!success)
                    {
                        _logger.LogError("Failed to disable Keycloak user {KeycloakId}: {Error}", user.KeycloakId, error);
                        continue;
                    }
                }

                await using var tx = await db.BeginTransactionAsync(ct);
                await UpdateLifecycleAsync(db, user.Id, status: "dormant", warningCount: 3,
                    lastWarnedAt: null, dormantSince: DateTime.UtcNow, confirmToken: null, ct);
                await SetUserDbStatusAsync(db, user.Id, "disabled", ct);
                await tx.CommitAsync(ct);

                await SendDormancyNoticeEmailAsync(user.Email, user.DisplayName);
                _logger.LogWarning("User {UserId} deactivated — no response to 3 lifecycle warnings", user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deactivate user {UserId}", user.Id);
            }
        }
    }

    private async Task PurgeDormantUsersAsync(Npgsql.NpgsqlConnection db, CancellationToken ct)
    {
        var cmd = new Npgsql.NpgsqlCommand($@"
            SELECT id, email, display_name, keycloak_id
            FROM users
            WHERE lifecycle_status = 'dormant'
              AND lifecycle_dormant_since < NOW() - INTERVAL '{LifecyclePolicyConstants.UserPurgeAfterDormantSqlInterval}'
        ", db);

        var users = await ReadUsersAsync(cmd, ct);
        _logger.LogInformation("Phase 5 (purge): {Count} user(s) to purge", users.Count);

        foreach (var user in users)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (!string.IsNullOrEmpty(user.KeycloakId))
                {
                    var (success, error) = await DeleteKeycloakUserAsync(user.KeycloakId);
                    if (!success)
                        _logger.LogError("Failed to delete Keycloak user {KeycloakId}: {Error}", user.KeycloakId, error);
                }

                await using var tx = await db.BeginTransactionAsync(ct);
                var deleteCmd = new Npgsql.NpgsqlCommand("DELETE FROM users WHERE id = @id", db);
                deleteCmd.Parameters.AddWithValue("id", user.Id);
                await deleteCmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogWarning("GDPR purge: user {UserId} ({Email}) permanently deleted", user.Id, user.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to purge user {UserId}", user.Id);
            }
        }
    }

    private static async Task<List<(Guid Id, string Email, string DisplayName, string? KeycloakId)>> ReadUsersAsync(
        Npgsql.NpgsqlCommand cmd, CancellationToken ct)
    {
        var results = new List<(Guid, string, string, string?)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        return results;
    }

    private static async Task UpdateLifecycleAsync(
        Npgsql.NpgsqlConnection db, Guid userId, string? status, int warningCount,
        DateTime? lastWarnedAt, DateTime? dormantSince, string? confirmToken, CancellationToken ct)
    {
        var cmd = new Npgsql.NpgsqlCommand(@"
            UPDATE users
            SET lifecycle_status = @status,
                lifecycle_warning_count = @warningCount,
                lifecycle_last_warned_at = @lastWarnedAt,
                lifecycle_dormant_since = @dormantSince,
                lifecycle_confirm_token = @confirmToken,
                updated_at = NOW()
            WHERE id = @id", db);
        cmd.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("warningCount", warningCount);
        cmd.Parameters.AddWithValue("lastWarnedAt", (object?)lastWarnedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("dormantSince", (object?)dormantSince ?? DBNull.Value);
        cmd.Parameters.AddWithValue("confirmToken", (object?)confirmToken ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task SetUserDbStatusAsync(Npgsql.NpgsqlConnection db, Guid userId, string status, CancellationToken ct)
    {
        var cmd = new Npgsql.NpgsqlCommand("UPDATE users SET status = @status, updated_at = NOW() WHERE id = @id", db);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<(bool success, string? error)> SetKeycloakUserEnabledAsync(string keycloakId, bool enabled)
        => await ExecuteKeycloakWithRetryAsync(async (http, token) =>
        {
            var req = new HttpRequestMessage(HttpMethod.Put,
                $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{keycloakId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new { enabled }), Encoding.UTF8, "application/json");
            return await http.SendAsync(req, new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);
        });

    private async Task<(bool success, string? error)> DeleteKeycloakUserAsync(string keycloakId)
        => await ExecuteKeycloakWithRetryAsync(async (http, token) =>
        {
            var req = new HttpRequestMessage(HttpMethod.Delete,
                $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{keycloakId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await http.SendAsync(req, new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);
        });

    private async Task<(bool success, string? error)> ExecuteKeycloakWithRetryAsync(
        Func<HttpClient, string, Task<HttpResponseMessage>> action)
    {
        const int maxRetries = 3;
        var token = await GetKcAdminTokenAsync();
        if (token == null) return (false, "Failed to get admin token");

        var http = _httpClientFactory.CreateClient();

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var res = await action(http, token);
                if (res.IsSuccessStatusCode) return (true, null);
                var err = await res.Content.ReadAsStringAsync();
                if ((int)res.StatusCode < 500) return (false, err);
                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                    continue;
                }
                return (false, err);
            }
            catch (Exception ex) when (attempt < maxRetries && ex is HttpRequestException or TaskCanceledException)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
        return (false, "Max retries exceeded");
    }

    private async Task<string?> GetKcAdminTokenAsync()
    {
        if (_kcAccessToken != null && DateTime.UtcNow < _kcTokenExpiry)
            return _kcAccessToken;

        await _tokenLock.WaitAsync();
        try
        {
            if (_kcAccessToken != null && DateTime.UtcNow < _kcTokenExpiry)
                return _kcAccessToken;

            var http = _httpClientFactory.CreateClient();
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _kc.BackendClientId,
                ["client_secret"] = _kc.BackendClientSecret
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var res = await http.PostAsync(
                $"{_kc.EffectiveInternalBaseUrl}/realms/{_kc.Realm}/protocol/openid-connect/token",
                content, cts.Token);

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get Keycloak admin token: {Status}", res.StatusCode);
                return null;
            }

            var json = await res.Content.ReadAsStringAsync(cts.Token);
            var tr = JsonSerializer.Deserialize<KcTokenResponse>(json);
            if (tr?.AccessToken == null) return null;

            _kcAccessToken = tr.AccessToken;
            _kcTokenExpiry = DateTime.UtcNow.AddSeconds(tr.ExpiresIn - 30);
            return _kcAccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obtaining Keycloak admin token");
            return null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task SendLifecycleWarningEmailAsync(string toEmail, string displayName, string confirmToken, int warningNumber)
    {
        var confirmLink = $"{AppBaseUrl}/api/account/confirm-activity?token={Uri.EscapeDataString(confirmToken)}";
        var urgency = warningNumber switch
        {
            1 => "We noticed you haven't logged in for a while.",
            2 => "This is a reminder — we still haven't heard from you.",
            _ => "This is your final reminder before your account is deactivated."
        };
        var subject = warningNumber == 1 ? "Is your account still active?"
            : $"Reminder: confirm your account activity ({warningNumber}/3)";

        var html = $@"<!DOCTYPE html><html><head><meta charset=""utf-8""></head>
<body style=""font-family:Arial,sans-serif;line-height:1.6;color:#333;max-width:600px;margin:0 auto;padding:20px"">
  <div style=""background:#3b82f6;padding:30px;text-align:center;border-radius:10px 10px 0 0"">
    <h1 style=""color:white;margin:0"">Account Activity Check</h1></div>
  <div style=""background:#f9f9f9;padding:30px;border-radius:0 0 10px 10px"">
    <p>Hi {displayName},</p><p>{urgency}</p>
    <div style=""text-align:center;margin:30px 0"">
      <a href=""{confirmLink}"" style=""background:#3b82f6;color:white;padding:14px 30px;text-decoration:none;border-radius:5px;font-weight:bold"">Yes, keep my account active</a>
    </div>
    <p style=""font-size:13px;color:#999"">After three reminders without response, your account will be deactivated and deleted after {LifecyclePolicyConstants.UserPurgeAfterDormantDays} days.</p>
  </div></body></html>";

        var text = $"Hi {displayName},\n\n{urgency}\n\nConfirm activity: {confirmLink}\n\nAfter three reminders, your account will be deleted after {LifecyclePolicyConstants.UserPurgeAfterDormantDays} days.";

        await SendEmailAsync(toEmail, displayName, subject, html, text);
    }

    private async Task SendDormancyNoticeEmailAsync(string toEmail, string displayName)
    {
        var html = $@"<!DOCTYPE html><html><head><meta charset=""utf-8""></head>
<body style=""font-family:Arial,sans-serif;line-height:1.6;color:#333;max-width:600px;margin:0 auto;padding:20px"">
  <div style=""background:#e67e22;padding:30px;text-align:center;border-radius:10px 10px 0 0"">
    <h1 style=""color:white;margin:0"">Account Deactivated</h1></div>
  <div style=""background:#f9f9f9;padding:30px;border-radius:0 0 10px 10px"">
    <p>Hi {displayName},</p>
    <p>Your account has been <strong>deactivated</strong> after three unanswered inactivity reminders.</p>
    <p>Your data will be <strong>permanently deleted in {LifecyclePolicyConstants.UserPurgeAfterDormantDays} days</strong>. Contact support to reactivate.</p>
    <p style=""font-size:13px;color:#999"">This action was taken in accordance with our data retention policy (GDPR Article 5).</p>
  </div></body></html>";

        var text = $"Hi {displayName},\n\nYour account has been deactivated after three unanswered inactivity reminders.\nData will be permanently deleted in {LifecyclePolicyConstants.UserPurgeAfterDormantDays} days. Contact support to reactivate.";

        await SendEmailAsync(toEmail, displayName, "Your account has been deactivated", html, text);
    }

    private async Task SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, string textBody)
    {
        const int maxRetries = 3;
        var smtp = _smtpConfig.Value;

        await _emailThrottle.WaitAsync();
        try
        {
            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var message = new MimeMessage();
                    message.From.Add(new MailboxAddress(smtp.FromName, smtp.FromEmail));
                    message.To.Add(new MailboxAddress(toName, toEmail));
                    message.Subject = subject;
                    message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody }.ToMessageBody();

                    using var client = new SmtpClient();
                    client.Timeout = 15_000;
                    await client.ConnectAsync(smtp.Host, smtp.Port,
                        smtp.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None);

                    if (!string.IsNullOrEmpty(smtp.Username) && !string.IsNullOrEmpty(smtp.Password))
                        await client.AuthenticateAsync(smtp.Username, smtp.Password);

                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                    _logger.LogInformation("Lifecycle email sent to {Email}: {Subject}", toEmail, subject);
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries)
                    {
                        _logger.LogError(ex, "Failed to send lifecycle email to {Email} after {Attempts} attempts", toEmail, maxRetries);
                        return;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                }
            }
        }
        finally
        {
            _emailThrottle.Release();
        }
    }

    private class KcTokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }
}
