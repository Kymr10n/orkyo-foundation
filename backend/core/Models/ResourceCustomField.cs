namespace Api.Models;

/// <summary>
/// The data types a custom field can hold. Deliberately small: these describe a value on a
/// form, they never drive matching, so nothing here needs an operator or a unit — that is
/// what criteria are for.
/// </summary>
public static class CustomFieldDataTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Date = "date";
    public const string Url = "url";

    /// <summary>Rows of its own, one set per resource, shaped by a list definition. Holds no value.</summary>
    public const string List = "list";

    /// <summary>Row ids picked from one shared list instance. Value is a JSON array of ids.</summary>
    public const string ListLookup = "list_lookup";

    /// <summary>
    /// What the API will create. Both list types are in: a `list` field owns its rows, a
    /// `list_lookup` field picks them out of a shared instance, and each is refused unless its
    /// binding names something that exists (see ResourceCustomFieldService.EnsureBindingAsync).
    /// </summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Text, Number, Boolean, Date, Url, List, ListLookup };

    /// <summary>The types whose values are validated as scalars against a jsonb document.</summary>
    public static readonly IReadOnlySet<string> Scalar =
        new HashSet<string>(StringComparer.Ordinal) { Text, Number, Boolean, Date, Url };
}

/// <summary>
/// A tenant-defined descriptive field on one resource type — a serial number, an asset tag,
/// a link to a datasheet. Values live on the resource in <c>custom_fields</c>, keyed by
/// <see cref="Key"/>.
/// </summary>
public record ResourceCustomFieldInfo
{
    public required Guid Id { get; init; }
    public required Guid ResourceTypeId { get; init; }
    /// <summary>Immutable after creation: it keys every value already stored for this field.</summary>
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    /// <summary>Immutable after creation: changing it would reinterpret stored values.</summary>
    public required string DataType { get; init; }
    public required bool IsRequired { get; init; }
    public required int SortOrder { get; init; }
    /// <summary>Inactive fields disappear from the form but keep the values already captured.</summary>
    public required bool IsActive { get; init; }
    /// <summary>For <see cref="CustomFieldDataTypes.List"/>: the shape this field's rows take.</summary>
    public Guid? ListDefinitionId { get; init; }
    /// <summary>For <see cref="CustomFieldDataTypes.ListLookup"/>: the shared instance rows are picked from.</summary>
    public Guid? ListInstanceId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record CreateResourceCustomFieldRequest
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required string DataType { get; init; }
    public bool IsRequired { get; init; }
    public int SortOrder { get; init; }
    /// <summary>Required for <c>list</c>, rejected otherwise — the binding is part of the shape.</summary>
    public Guid? ListDefinitionId { get; init; }
    /// <summary>Required for <c>list_lookup</c>, rejected otherwise.</summary>
    public Guid? ListInstanceId { get; init; }
}

/// <summary>
/// Partial update. <see cref="ResourceCustomFieldInfo.Key"/> and
/// <see cref="ResourceCustomFieldInfo.DataType"/> are absent on purpose — see their remarks.
/// </summary>
public record UpdateResourceCustomFieldRequest
{
    public string? Label { get; init; }
    public string? Description { get; init; }
    public bool? IsRequired { get; init; }
    public int? SortOrder { get; init; }
    public bool? IsActive { get; init; }
}
