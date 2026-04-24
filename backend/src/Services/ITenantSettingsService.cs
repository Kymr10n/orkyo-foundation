using Api.Models;

namespace Api.Services;

/// <summary>
/// Resolves and mutates tenant-scoped settings for the current context.
/// The contract is product-agnostic: multi-tenant SaaS and single-tenant Community both
/// consume the same interface; the concrete implementation differs only in how the current
/// tenant context is resolved. Implementations live in composition layers.
/// </summary>
public interface ITenantSettingsService
{
    /// <summary>
    /// Get the resolved settings for the current context.
    /// Returns compiled defaults with scope-appropriate DB overrides applied.
    /// </summary>
    Task<TenantSettings> GetSettingsAsync();

    /// <summary>
    /// Update one or more settings. Returns the full resolved settings after the update.
    /// </summary>
    Task<TenantSettings> UpdateSettingsAsync(Dictionary<string, string> updates);

    /// <summary>
    /// Delete a setting override, reverting it to the compiled default.
    /// </summary>
    Task<bool> ResetSettingAsync(string key);

    /// <summary>
    /// Get metadata descriptors for all known settings (for the settings UI).
    /// </summary>
    IReadOnlyList<TenantSettingDescriptor> GetDescriptors();
}
