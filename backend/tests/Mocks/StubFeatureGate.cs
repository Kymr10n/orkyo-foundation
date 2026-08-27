using Api.Helpers;
using Api.Security.Features;

namespace Orkyo.Foundation.Tests.Mocks;

/// <summary>
/// Feature gate for the test host. Behaves exactly like <see cref="AllFeaturesEnabledGate"/>
/// — foundation's own fallback — until a test disables a key.
///
/// Every entitlement refusal in the application is otherwise unreachable under test: the
/// foundation host enables everything, so the "upgrade required" branches only ever run in
/// SaaS. Disable a key here to reach them, and restore it afterwards.
/// </summary>
public sealed class StubFeatureGate : IFeatureGate
{
    private readonly HashSet<string> _disabled = [];

    /// <summary>Turn one feature off for the duration of a test. Not thread-safe by design.</summary>
    public void Disable(string featureKey) => _disabled.Add(featureKey);

    /// <summary>Restore a feature disabled by <see cref="Disable"/>.</summary>
    public void Enable(string featureKey) => _disabled.Remove(featureKey);

    public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) =>
        Task.FromResult(!_disabled.Contains(featureKey));

    public Task EnsureEnabledAsync(string featureKey, CancellationToken ct = default) =>
        _disabled.Contains(featureKey)
            ? throw new FeatureNotAvailableException(featureKey, "disabled for this test")
            : Task.CompletedTask;
}
