namespace Api.Models;

public record ResourceGroupMembersResponse
{
    public required Guid GroupId { get; init; }
    public required List<ResourceInfo> Members { get; init; }
}

public record SetResourceGroupMembersRequest
{
    public required List<Guid> ResourceIds { get; init; }
}
