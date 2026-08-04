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

/// <summary>
/// How a criterion presents for ONE resource type. Per-type rather than per-criterion because
/// the same attribute can be mandatory for one type and optional for another — a serial number
/// is required on a tool but meaningless on a space.
/// </summary>
public record CriterionTypeDisplay
{
    public required string ResourceTypeKey { get; init; }
    public required bool IsRequired { get; init; }
    public required int SortOrder { get; init; }
    /// <summary>Render on the resource create/edit form. False = assignable, but not on the form.</summary>
    public required bool ShowOnForm { get; init; }
}

public record UpdateCriterionApplicabilityRequest
{
    public bool? ApplicableToRequests { get; init; }
    public List<string>? ResourceTypeKeys { get; init; }
}
