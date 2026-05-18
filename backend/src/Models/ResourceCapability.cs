using System.Text.Json;
using Api.Repositories;

namespace Api.Models;

public record ResourceCapabilityInfo
{
    public required Guid Id { get; init; }
    public required Guid ResourceId { get; init; }
    public required Guid CriterionId { get; init; }
    public required JsonElement Value { get; init; }
    public CriterionMetadata? Criterion { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record UpsertResourceCapabilityRequest
{
    public required Guid CriterionId { get; init; }
    public required JsonElement Value { get; init; }
}

public record CriterionApplicabilityInfo
{
    public required Guid CriterionId { get; init; }
    public required bool ApplicableToRequests { get; init; }
    public required List<string> ResourceTypeKeys { get; init; }
}

public record UpdateCriterionApplicabilityRequest
{
    public bool? ApplicableToRequests { get; init; }
    public List<string>? ResourceTypeKeys { get; init; }
}
