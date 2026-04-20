namespace Api.Constants;

/// <summary>
/// Search algorithm constants for tuning search behavior.
/// Used by SearchRepository for full-text and trigram similarity queries.
/// </summary>
public static class SearchConstants
{
    /// <summary>Minimum query length for full search (below this, exact prefix match only)</summary>
    public const int MinQueryLengthForFullSearch = 3;

    /// <summary>Trigram similarity threshold for primary matches (combined FTS + trigram queries)</summary>
    public const double PrimarySimilarityThreshold = 0.2;

    /// <summary>Trigram similarity threshold for secondary (broader) matches (short query / trigram-only)</summary>
    public const double SecondarySimilarityThreshold = 0.15;
}
