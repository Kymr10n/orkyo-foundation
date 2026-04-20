namespace Api.Helpers;

/// <summary>
/// Exception thrown when a feature is not available in the current context.
/// Foundation code throws this without knowing about tiers.
/// </summary>
public class FeatureNotAvailableException : Exception
{
    public string Feature { get; }

    public FeatureNotAvailableException(string feature, string reason)
        : base($"Feature '{feature}' is not available: {reason}")
    {
        Feature = feature;
    }

    public FeatureNotAvailableException(string feature, string currentTier, string requiredTier)
        : base($"Feature '{feature}' requires {requiredTier} tier or above. Current tier: {currentTier}.")
    {
        Feature = feature;
    }
}
