namespace Api.Helpers;

/// <summary>
/// Thrown when a write carried an <c>If-Match</c> version and the row has changed since the
/// caller read it. Maps to HTTP 412 at the endpoint boundary.
/// </summary>
public class PreconditionFailedException : Exception
{
    public PreconditionFailedException(string resourceType)
        : base($"The {resourceType} was changed by someone else. Reload it and try again.") { }
}
