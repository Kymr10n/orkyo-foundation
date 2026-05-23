using Api.Services;

namespace Api.Configuration;

/// <summary>
/// Composes <see cref="DeploymentConfig"/> (immutable, env-sourced) with
/// <see cref="RuntimeConfig"/> (mutable, DB-sourced) into a single view.
/// Injected where services need both deployment facts and runtime settings.
/// </summary>
public sealed class EffectiveConfig
{
    private readonly DeploymentConfig _deployment;
    private readonly ISiteSettingsService _siteSettings;

    public EffectiveConfig(DeploymentConfig deployment, ISiteSettingsService siteSettings)
    {
        _deployment = deployment;
        _siteSettings = siteSettings;
    }

    public DeploymentConfig Deployment => _deployment;

    public Task<RuntimeConfig> GetRuntimeAsync() => _siteSettings.GetRuntimeConfigAsync();
}
