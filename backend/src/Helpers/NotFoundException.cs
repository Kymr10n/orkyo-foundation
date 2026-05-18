namespace Api.Helpers;

/// <summary>
/// Thrown when a requested resource does not exist. Maps to HTTP 404 at the endpoint boundary.
/// Prefer this over <see cref="KeyNotFoundException"/> or string-matched <see cref="InvalidOperationException"/>
/// so the exception-to-HTTP mapping in <see cref="EndpointHelpers"/> is type-safe.
/// </summary>
public class NotFoundException : Exception
{
    public string ResourceType { get; }
    public Guid? ResourceId { get; }

    public NotFoundException(string resourceType, Guid id)
        : base($"{resourceType} with ID {id} not found")
    {
        ResourceType = resourceType;
        ResourceId = id;
    }

    public NotFoundException(string message)
        : base(message)
    {
        ResourceType = string.Empty;
    }
}
