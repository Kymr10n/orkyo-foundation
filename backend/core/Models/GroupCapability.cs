using System.Text.Json;

namespace Api.Models;

public record GroupCapabilityInfo
{
    public Guid Id { get; init; }
    public Guid GroupId { get; init; }
    public Guid CriterionId { get; init; }
    public JsonElement Value { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public CriterionMetadata? Criterion { get; init; }
}
