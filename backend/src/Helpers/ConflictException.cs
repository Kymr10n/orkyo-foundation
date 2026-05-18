namespace Api.Helpers;

/// <summary>
/// Thrown when an operation is rejected due to a conflicting state (e.g. unique constraint violation,
/// circular reference). Maps to HTTP 409 at the endpoint boundary.
/// Prefer this over string-matched <see cref="InvalidOperationException"/> so the
/// exception-to-HTTP mapping in <see cref="EndpointHelpers"/> is type-safe.
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
