namespace Api.Constants;

/// <summary>
/// Standard API error code constants.
/// These provide stable, machine-readable error identifiers for API consumers.
/// </summary>
public static class ErrorCodes
{
    /// <summary>Resource not found (404)</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>Validation error (400)</summary>
    public const string ValidationError = "VALIDATION_ERROR";

    /// <summary>Conflict error (409)</summary>
    public const string Conflict = "CONFLICT";
}
