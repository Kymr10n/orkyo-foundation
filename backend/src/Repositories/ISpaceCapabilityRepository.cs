namespace Api.Repositories;

public interface ISpaceCapabilityRepository
{
    Task<List<SpaceCapabilityInfo>> GetAllAsync(Guid siteId, Guid spaceId);
    Task<SpaceCapabilityInfo> CreateAsync(Guid siteId, Guid spaceId, Guid criterionId, object value);
    Task<SpaceCapabilityInfo?> UpdateAsync(Guid siteId, Guid spaceId, Guid capabilityId, object value);
    Task<bool> DeleteAsync(Guid siteId, Guid spaceId, Guid capabilityId);
}

public record SpaceCapabilityInfo
{
    public Guid Id { get; init; }
    public Guid SpaceId { get; init; }
    public Guid CriterionId { get; init; }
    public object? Value { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public CriterionMetadata? Criterion { get; init; }
}

public record CriterionMetadata
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string DataType { get; init; }
    public string? Unit { get; init; }
}
