namespace Api.Helpers;

/// <summary>
/// Exception thrown when an org exceeds a resource quota.
/// Foundation code throws this without knowing about tiers.
/// </summary>
public class QuotaExceededException : Exception
{
    public string ResourceType { get; }
    public int Limit { get; }

    public QuotaExceededException(string resourceType, int limit)
        : base($"Quota exceeded for {resourceType}. Maximum allowed: {limit}")
    {
        ResourceType = resourceType;
        Limit = limit;
    }

    public QuotaExceededException(string resourceType, int limit, string message)
        : base(message)
    {
        ResourceType = resourceType;
        Limit = limit;
    }
}

/// <summary>
/// Backward-compatible alias. SaaS code can still use this for tier-specific messaging.
/// </summary>
public class TierLimitExceededException : QuotaExceededException
{
    public string TierName { get; }

    public TierLimitExceededException(string tierName, string resourceType, int limit)
        : base(resourceType, limit, $"Service tier '{tierName}' limit exceeded for {resourceType}. Maximum allowed: {limit}")
    {
        TierName = tierName;
    }
}
