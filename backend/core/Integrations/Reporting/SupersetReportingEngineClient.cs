using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Reporting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Integrations.Reporting;

/// <summary>
/// Superset REST adapter. Authenticates as the configured service account, caches the
/// access token, and exposes the three operations the provisioner and embed service need.
///
/// All Superset-specific DTOs are internal to this file — callers only see the
/// <see cref="IReportingEngineClient"/> contract.
/// </summary>
public sealed class SupersetReportingEngineClient : IReportingEngineClient
{
    private readonly HttpClient _http;
    private readonly ReportingOptions _opts;
    private readonly ILogger<SupersetReportingEngineClient> _logger;

    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private string? _csrfToken;
    private DateTime _csrfTokenExpiry = DateTime.MinValue;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public SupersetReportingEngineClient(
        HttpClient http,
        IOptions<ReportingOptions> opts,
        ILogger<SupersetReportingEngineClient> logger)
    {
        _http = http;
        _opts = opts.Value;
        _logger = logger;
    }

    // ── Guest token ───────────────────────────────────────────────────────────

    public async Task<string> CreateGuestTokenAsync(Guid dashboardUuid, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var csrf = await GetCsrfTokenAsync(ct);

        var body = new
        {
            user = new { username = "guest", first_name = "Guest", last_name = "User" },
            resources = new[] { new { type = "dashboard", id = dashboardUuid.ToString() } },
            rls = Array.Empty<object>(),
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl}/api/v1/security/guest_token/");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.TryAddWithoutValidation("X-CSRFToken", csrf);
        req.Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(resp, "Failed to create Superset guest token");

        var result = await resp.Content.ReadFromJsonAsync<GuestTokenResponse>(_json, ct)
            ?? throw new InvalidOperationException("Superset returned empty guest token response");

        return result.Token;
    }

    // ── Datasource ────────────────────────────────────────────────────────────

    public async Task<Guid> EnsureDatabaseAsync(
        string databaseName,
        string sqlAlchemyUri,
        CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);

        // Try to find an existing DB with this name.
        var existing = await FindDatabaseByNameAsync(databaseName, token, ct);
        if (existing.HasValue)
        {
            _logger.LogDebug("Superset database '{Name}' already exists with UUID {Uuid}", databaseName, existing.Value);
            return existing.Value;
        }

        var csrf = await GetCsrfTokenAsync(ct);

        // Create new.
        var body = new
        {
            database_name = databaseName,
            sqlalchemy_uri = sqlAlchemyUri,
            expose_in_sqllab = false,
            allow_ctas = false,
            allow_cvas = false,
            allow_dml = false,
            allow_run_async = false,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl}/api/v1/database/");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.TryAddWithoutValidation("X-CSRFToken", csrf);
        req.Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(resp, $"Failed to create Superset database '{databaseName}'");

        var result = await resp.Content.ReadFromJsonAsync<DatabaseCreateResponse>(_json, ct)
            ?? throw new InvalidOperationException("Superset returned empty database create response");

        var uuid = Guid.Parse(result.Result.Uuid);
        _logger.LogInformation("Created Superset database '{Name}' with UUID {Uuid}", databaseName, uuid);
        return uuid;
    }

    // ── Dashboard copy ────────────────────────────────────────────────────────

    public async Task<Guid> CopyDashboardAsync(
        Guid templateDashboardUuid,
        string newTitle,
        CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var csrf = await GetCsrfTokenAsync(ct);

        // Resolve template dashboard integer PK from UUID.
        var pk = await GetDashboardPkByUuidAsync(templateDashboardUuid, token, ct);

        // Superset's copy endpoint requires both dashboard_title and json_metadata.
        var copyBody = JsonSerializer.Serialize(new
        {
            dashboard_title = newTitle,
            json_metadata = "{}",
        }, _json);

        using var copyReq = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl}/api/v1/dashboard/{pk}/copy/");
        copyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        copyReq.Headers.TryAddWithoutValidation("X-CSRFToken", csrf);
        copyReq.Content = new StringContent(copyBody, Encoding.UTF8, "application/json");

        using var copyResp = await _http.SendAsync(copyReq, ct);
        await EnsureSuccessAsync(copyResp, $"Failed to copy Superset dashboard {templateDashboardUuid}");

        var copyResult = await copyResp.Content.ReadFromJsonAsync<DashboardCopyResponse>(_json, ct)
            ?? throw new InvalidOperationException("Superset returned empty dashboard copy response");

        // Enable embedded mode for the new dashboard and retrieve its UUID in one request.
        // The PUT /embedded endpoint creates the embedded config and returns the UUID that
        // the @superset-ui/embedded-sdk uses to scope guest tokens.
        var newUuid = await EnableEmbedAndGetUuidAsync(copyResult.Result.Id, token, csrf, ct);

        _logger.LogInformation("Copied Superset dashboard {Template} → {New} (title: {Title})",
            templateDashboardUuid, newUuid, newTitle);
        return newUuid;
    }

    private async Task<Guid> EnableEmbedAndGetUuidAsync(int pk, string token, string csrf, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"{_opts.BaseUrl}/api/v1/dashboard/{pk}/embedded");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.TryAddWithoutValidation("X-CSRFToken", csrf);
        req.Content = new StringContent("{\"allowed_domains\": []}", Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(resp, $"Failed to enable embedded mode for Superset dashboard PK {pk}");

        var result = await resp.Content.ReadFromJsonAsync<EmbeddedConfigResponse>(_json, ct)
            ?? throw new InvalidOperationException($"Superset returned empty embedded config for dashboard {pk}");

        return Guid.Parse(result.Result.Uuid);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<string> GetCsrfTokenAsync(CancellationToken ct)
    {
        if (_csrfToken is not null && DateTime.UtcNow < _csrfTokenExpiry)
            return _csrfToken;

        var accessToken = await GetAccessTokenAsync(ct);

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_opts.BaseUrl}/api/v1/security/csrf_token/");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(resp, "Failed to fetch Superset CSRF token");

        var result = await resp.Content.ReadFromJsonAsync<CsrfTokenResponse>(_json, ct)
            ?? throw new InvalidOperationException("Superset returned empty CSRF token response");

        // Cache conservatively — the CSRF token is a short-lived JWT; 25 minutes is safe.
        _csrfTokenExpiry = DateTime.UtcNow.AddMinutes(25);
        _csrfToken = result.Result;
        return _csrfToken;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && DateTime.UtcNow < _tokenExpiry)
            return _accessToken;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl}/api/v1/security/login");
        req.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                username = _opts.AdminUsername,
                password = _opts.AdminPassword,
                provider = "db",
                refresh = true,
            }, _json),
            Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(resp, "Failed to authenticate with Superset");

        var result = await resp.Content.ReadFromJsonAsync<LoginResponse>(_json, ct)
            ?? throw new InvalidOperationException("Superset login returned empty response");

        // Superset JWTs are typically valid for 6 hours; cache with a 5-minute safety margin.
        _tokenExpiry = DateTime.UtcNow.AddHours(6).AddMinutes(-5);
        _accessToken = result.AccessToken;
        // Invalidate the CSRF token so it is re-fetched for the new session.
        _csrfToken = null;
        _csrfTokenExpiry = DateTime.MinValue;
        return _accessToken;
    }

    private async Task<Guid?> FindDatabaseByNameAsync(string name, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_opts.BaseUrl}/api/v1/database/");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var result = await resp.Content.ReadFromJsonAsync<DatabaseListResponse>(_json, ct);
        var match = result?.Result.FirstOrDefault(d =>
            string.Equals(d.DatabaseName, name, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : Guid.Parse(match.Uuid);
    }

    private async Task<int> GetDashboardPkByUuidAsync(Guid uuid, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_opts.BaseUrl}/api/v1/dashboard/{uuid}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(resp, $"Dashboard {uuid} not found in Superset");

        var result = await resp.Content.ReadFromJsonAsync<DashboardGetResponse>(_json, ct)
            ?? throw new InvalidOperationException($"Superset returned empty response for dashboard {uuid}");

        return result.Result.Id;

    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, string context)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync();
        throw new InvalidOperationException(
            $"{context} — HTTP {(int)resp.StatusCode}: {body[..Math.Min(body.Length, 300)]}");
    }

    // ── Internal DTOs ─────────────────────────────────────────────────────────

    private sealed record LoginResponse([property: JsonPropertyName("access_token")] string AccessToken);
    private sealed record CsrfTokenResponse([property: JsonPropertyName("result")] string Result);

    private sealed record GuestTokenResponse([property: JsonPropertyName("token")] string Token);

    private sealed record DatabaseCreateResponse(
        [property: JsonPropertyName("result")] DatabaseResultDto Result);

    private sealed record DatabaseResultDto(
        [property: JsonPropertyName("uuid")] string Uuid);

    private sealed record DatabaseListResponse(
        [property: JsonPropertyName("result")] List<DatabaseListItem> Result);

    private sealed record DatabaseListItem(
        [property: JsonPropertyName("database_name")] string DatabaseName,
        [property: JsonPropertyName("uuid")] string Uuid);

    // GET /api/v1/dashboard/{id_or_slug} — returns the full dashboard shape; uuid is NOT in the response
    private sealed record DashboardGetResponse(
        [property: JsonPropertyName("result")] DashboardGetResultDto Result);

    private sealed record DashboardGetResultDto(
        [property: JsonPropertyName("id")] int Id);

    // POST /api/v1/dashboard/{pk}/copy/ — returns only id + last_modified_time
    private sealed record DashboardCopyResponse(
        [property: JsonPropertyName("result")] DashboardCopyResultDto Result);

    private sealed record DashboardCopyResultDto(
        [property: JsonPropertyName("id")] int Id);

    // PUT /api/v1/dashboard/{pk}/embedded — enables embedding and returns the UUID
    private sealed record EmbeddedConfigResponse(
        [property: JsonPropertyName("result")] EmbeddedConfigDto Result);

    private sealed record EmbeddedConfigDto(
        [property: JsonPropertyName("uuid")] string Uuid);
}
