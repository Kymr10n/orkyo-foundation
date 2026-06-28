using Api.Models;

namespace Api.Services;

/// <summary>
/// <see cref="ITenantSettingsService"/> for background-worker hosts, which run outside any per-tenant
/// context. Reads return compiled defaults (so email branding falls back to the platform default);
/// the mutating methods are never invoked from a worker. Shared by the saas and community workers.
/// </summary>
public sealed class WorkerTenantSettingsService : ITenantSettingsService
{
    public Task<TenantSettings> GetSettingsAsync(CancellationToken ct = default) =>
        Task.FromResult(new TenantSettings());

    public Task<TenantSettings> UpdateSettingsAsync(Dictionary<string, string> updates, CancellationToken ct = default) =>
        throw new NotSupportedException("Worker does not mutate tenant settings.");

    public Task<bool> ResetSettingAsync(string key, CancellationToken ct = default) =>
        throw new NotSupportedException("Worker does not mutate tenant settings.");

    public IReadOnlyList<TenantSettingDescriptor> GetDescriptors() => [];
}
