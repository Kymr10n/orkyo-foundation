namespace Api.Constants;

/// <summary>
/// Search algorithm constants for tuning search behavior.
/// Used by SearchRepository for full-text and trigram similarity queries.
/// </summary>
public static class SearchConstants
{
    /// <summary>Minimum query length for full search (below this, exact prefix match only)</summary>
    public const int MinQueryLengthForFullSearch = 3;
}
