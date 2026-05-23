using System.Text.Json.Serialization;

namespace Api.Integrations.Keycloak;

/// <summary>
/// Keycloak Admin API session DTO. Wire-format owned by Keycloak and identical
/// across multi-tenant SaaS and single-tenant Community deployments.
/// </summary>
public class KeycloakSession
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("ipAddress")]
    public string IpAddress { get; set; } = string.Empty;

    [JsonPropertyName("start")]
    public long Start { get; set; }

    [JsonPropertyName("lastAccess")]
    public long LastAccess { get; set; }

    [JsonPropertyName("clients")]
    public Dictionary<string, string>? Clients { get; set; }

    // Computed properties for frontend
    public DateTime StartTime => DateTimeOffset.FromUnixTimeMilliseconds(Start).DateTime;
    public DateTime LastAccessTime => DateTimeOffset.FromUnixTimeMilliseconds(LastAccess).DateTime;
}

/// <summary>
/// MFA enrollment status surfaced by the Keycloak admin pipeline.
/// </summary>
public class MfaStatus
{
    public bool TotpEnabled { get; set; }
    public string? TotpCredentialId { get; set; }
    public DateTime? TotpCreatedDate { get; set; }
    public string? TotpLabel { get; set; }
    public bool RecoveryCodesConfigured { get; set; }
    public string? RecoveryCodesCredentialId { get; set; }
}

/// <summary>
/// User profile fields surfaced by the Keycloak admin pipeline.
/// </summary>
public class UserProfile
{
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
}
