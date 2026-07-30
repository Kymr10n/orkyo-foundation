using System.Text.Json;

namespace Api.Models;

/// <summary>
/// A custom field definition belonging to a resource type. Values for these fields are stored
/// per resource in <c>resources.metadata_json</c>, keyed by <see cref="Key"/>.
/// </summary>
public record ResourceTypeFieldInfo
{
    public required Guid Id { get; init; }
    public required Guid ResourceTypeId { get; init; }
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required string DataType { get; init; }
    /// <summary>For <c>select</c> fields: <c>{"values":["a","b"]}</c>.</summary>
    public JsonElement? Options { get; init; }
    /// <summary>Optional constraints: <c>{"min":..,"max":..,"regex":"..","maxLength":..}</c>.</summary>
    public JsonElement? Validation { get; init; }
    public required bool IsRequired { get; init; }
    public required int SortOrder { get; init; }
    public required bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record CreateResourceTypeFieldRequest
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required string DataType { get; init; }
    public JsonElement? Options { get; init; }
    public JsonElement? Validation { get; init; }
    public bool IsRequired { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>
/// Mutable parts of a field definition. <c>Key</c> and <c>DataType</c> are deliberately absent:
/// changing either would invalidate values already stored against the field. Deactivate the field
/// and create a replacement instead.
/// </summary>
public record UpdateResourceTypeFieldRequest
{
    public string? Label { get; init; }
    public string? Description { get; init; }
    public JsonElement? Options { get; init; }
    public JsonElement? Validation { get; init; }
    public bool? IsRequired { get; init; }
    public int? SortOrder { get; init; }
    public bool? IsActive { get; init; }
}

/// <summary>A single problem found while validating a metadata document.</summary>
public record MetadataValidationIssue
{
    /// <summary>The offending field key, or null when the issue is document-wide.</summary>
    public string? FieldKey { get; init; }
    public required string Message { get; init; }
}

/// <summary>Outcome of validating a metadata document against a type's field definitions.</summary>
public record MetadataValidationResult
{
    public required List<MetadataValidationIssue> Blockers { get; init; }
    public required List<MetadataValidationIssue> Warnings { get; init; }
    public bool IsValid => Blockers.Count == 0;

    public static MetadataValidationResult Empty() => new() { Blockers = [], Warnings = [] };
}
