using System.Text.Json.Serialization;
using Api.Constants;

namespace Api.Models;

/// <summary>
/// Data type for criterion values.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CriterionDataType
{
    Boolean,
    Number,
    String,
    Enum
}

/// <summary>
/// Definition of a criterion that can be used as a space capability or job requirement.
/// Criteria are tenant-wide reusable definitions.
/// </summary>
public record CriterionInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required CriterionDataType DataType { get; init; }

    /// <summary>
    /// Optional enum values for Enum type criteria (JSON array).
    /// Example: ["S", "M", "L", "XL"] for size classes.
    /// </summary>
    public List<string>? EnumValues { get; init; }

    /// <summary>
    /// Optional unit for Number type criteria (e.g., "kg", "kW", "m²").
    /// </summary>
    public string? Unit { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Request to create a new criterion.
/// </summary>
public record CreateCriterionRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required CriterionDataType DataType { get; init; }
    public List<string>? EnumValues { get; init; }
    public string? Unit { get; init; }
}

/// <summary>
/// Request to update an existing criterion.
/// </summary>
public record UpdateCriterionRequest
{
    public string? Description { get; init; }
    public List<string>? EnumValues { get; init; }
    public string? Unit { get; init; }
}
