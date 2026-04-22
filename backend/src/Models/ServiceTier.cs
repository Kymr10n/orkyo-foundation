namespace Api.Models;

/// <summary>
/// Defines the available service tiers for the application.
/// Only Free tier is active at launch. Professional and Enterprise exist for future extensibility.
/// </summary>
public enum ServiceTier
{
    /// <summary>
    /// Free tier with limited resources (5 users, 1 site, 15 spaces).
    /// </summary>
    Free = 0,

    /// <summary>
    /// Professional tier with expanded limits (50 users, 5 sites, 250 spaces).
    /// Not yet selectable - reserved for future monetization.
    /// </summary>
    Professional = 1,

    /// <summary>
    /// Enterprise tier with no resource limits.
    /// Not yet selectable - reserved for future monetization.
    /// </summary>
    Enterprise = 2
}