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
    // Value matches what has always been emitted on the wire (formerly via nameof); aligning the
    // casing to "VALIDATION_ERROR" is deferred to the next major (deliberate — breaking change).
    public const string ValidationError = "ValidationError";

    /// <summary>Conflict error (409)</summary>
    public const string Conflict = "CONFLICT";

    /// <summary>Unprocessable entity (422)</summary>
    public const string UnprocessableEntity = "UNPROCESSABLE_ENTITY";
}
