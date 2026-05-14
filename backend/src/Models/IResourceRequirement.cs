namespace Api.Models;

public interface IResourceRequirement
{
    Guid ResourceTypeId { get; }
    int Count { get; }
    IReadOnlyList<Guid> RequiredCriterionIds { get; }
}

public sealed record SpaceResourceRequirement(
    Guid ResourceTypeId,
    IReadOnlyList<Guid> RequiredCriterionIds) : IResourceRequirement
{
    public int Count => 1;
}
