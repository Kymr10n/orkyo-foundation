using System.Text.Json;

namespace Api.Models;

/// <summary>
/// The data types a list column can hold: the five a custom field has, plus <c>select</c> and
/// <c>row_ref</c>.
/// </summary>
/// <remarks>
/// <c>select</c> exists here and not on <see cref="CustomFieldDataTypes"/> on purpose — migration
/// 1770 deferred it for scalar fields because an options editor was an unasked UI question, and a
/// list column is where that question got answered. Keeping the sets separate means the deferral
/// still holds where it was made. <c>row_ref</c> stays here for the same reason: it means "another
/// row of this same list", which is a sentence only a list column can say.
/// </remarks>
public static class ListColumnDataTypes
{
    public const string Text = CustomFieldDataTypes.Text;
    public const string Number = CustomFieldDataTypes.Number;
    public const string Boolean = CustomFieldDataTypes.Boolean;
    public const string Date = CustomFieldDataTypes.Date;
    public const string Url = CustomFieldDataTypes.Url;
    public const string Select = "select";

    /// <summary>
    /// The id of another row of the same instance — what makes a list a tree. Validated by
    /// <see cref="IListRowService"/> rather than by the database: rows live in one table with no
    /// per-instance identity to point a foreign key at, so existence, self-reference and cycles
    /// are checked on write.
    /// </summary>
    public const string RowRef = "row_ref";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Text, Number, Boolean, Date, Url, Select, RowRef };
}

/// <summary>Which of the two things a <see cref="ListInstanceInfo"/> is. See migration 1780.</summary>
public static class ListInstanceKinds
{
    /// <summary>Admin-created, named, referenced by many resources through a lookup field.</summary>
    public const string Shared = "shared";

    /// <summary>Belongs to one (resource, field) pair, created lazily by the resolver.</summary>
    public const string Resource = "resource";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Shared, Resource };
}

/// <summary>Who owns a <see cref="ListDefinitionInfo"/>. See migration 1810.</summary>
public static class ListDefinitionScopes
{
    /// <summary>Owned by one resource type; edited from that type's Lists tab.</summary>
    public const string Resource = "resource";

    /// <summary>Organization master data — departments, job titles, cost centres.</summary>
    public const string Organization = "organization";

    /// <summary>Shared reference data with no narrower owner — countries, units.</summary>
    public const string Common = "common";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Resource, Organization, Common };
}

/// <summary>
/// The reusable half of a list: what columns it has and what they hold. Bound to a custom field
/// (per-resource rows) or instantiated as a named shared instance.
/// </summary>
public record ListDefinitionInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    /// <summary>One of <see cref="ListDefinitionScopes"/>. Immutable — see the service.</summary>
    public required string Scope { get; init; }
    /// <summary>Set exactly when <see cref="Scope"/> is <c>resource</c>; null otherwise.</summary>
    public Guid? ResourceTypeId { get; init; }
    /// <summary>Deactivating retires a definition from new bindings; delete is RESTRICTed while in use.</summary>
    public required bool IsActive { get; init; }
    /// <summary>
    /// The column that identifies a row wherever one is shown as a single value — the row picker,
    /// a phone card heading. Null falls back to the first active column, which is what every
    /// surface did before this existed, so an unset definition behaves as it always has.
    /// </summary>
    public Guid? DisplayColumnId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    /// <summary>Populated on single reads; empty on list reads.</summary>
    public IReadOnlyList<ListColumnInfo> Columns { get; init; } = [];
}

/// <summary>One column of a list definition — the shape of one cell in every row.</summary>
public record ListColumnInfo
{
    public required Guid Id { get; init; }
    public required Guid ListDefinitionId { get; init; }
    /// <summary>Immutable after creation: it keys this column's value in every stored row.</summary>
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    /// <summary>Immutable after creation: changing it would reinterpret stored cells.</summary>
    public required string DataType { get; init; }
    /// <summary>Declared options for <c>select</c>, null otherwise. Editable — see remarks.</summary>
    /// <remarks>
    /// Removing an option never rewrites stored rows: options are validated on write only, the same
    /// contract criteria enum values have. A row written under an option that later disappears keeps
    /// its value and simply cannot be re-saved unchanged.
    /// </remarks>
    public IReadOnlyList<string>? Options { get; init; }
    public required bool IsRequired { get; init; }
    /// <summary>Form order of the fields in the row dialog.</summary>
    public required int SortOrder { get; init; }
    public required bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// A data holder built from a definition. <see cref="Name"/> is set for shared instances;
/// <see cref="ResourceId"/> and <see cref="FieldId"/> for per-resource ones. Never both.
/// </summary>
public record ListInstanceInfo
{
    public required Guid Id { get; init; }
    public required Guid ListDefinitionId { get; init; }
    public required string Kind { get; init; }
    public string? Name { get; init; }
    public Guid? ResourceId { get; init; }
    public Guid? FieldId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>One row of one instance. Cells keyed by <see cref="ListColumnInfo.Key"/>.</summary>
public record ListRowInfo
{
    public required Guid Id { get; init; }
    public required Guid ListInstanceId { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Values { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record CreateListDefinitionRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    /// <summary>Defaults to <c>common</c>, matching what every definition was before 1810.</summary>
    public string Scope { get; init; } = ListDefinitionScopes.Common;
    /// <summary>Required for the <c>resource</c> scope, rejected for the other two.</summary>
    public Guid? ResourceTypeId { get; init; }
}

/// <summary>Partial update. Name and description only — columns have their own endpoints.</summary>
public record UpdateListDefinitionRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public bool? IsActive { get; init; }
    /// <summary>
    /// The column to show wherever a row is one value. Must belong to this definition. Absent
    /// leaves the current designation alone; <see cref="ClearDisplayColumn"/> removes it, because
    /// null here cannot mean both "unchanged" and "cleared".
    /// </summary>
    public Guid? DisplayColumnId { get; init; }
    public bool ClearDisplayColumn { get; init; }
}

public record CreateListColumnRequest
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required string DataType { get; init; }
    public IReadOnlyList<string>? Options { get; init; }
    public bool IsRequired { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>
/// Partial update. <see cref="ListColumnInfo.Key"/> and <see cref="ListColumnInfo.DataType"/> are
/// absent on purpose — see their remarks. Options stay updatable.
/// </summary>
public record UpdateListColumnRequest
{
    public string? Label { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? Options { get; init; }
    public bool? IsRequired { get; init; }
    public int? SortOrder { get; init; }
    public bool? IsActive { get; init; }
}

/// <summary>Creates a shared instance. Per-resource instances are created by the resolver, not here.</summary>
public record CreateListInstanceRequest
{
    public required string Name { get; init; }
}

public record UpdateListInstanceRequest
{
    public string? Name { get; init; }
}

/// <summary>A row carries only its cells; which instance it belongs to comes from the route.</summary>
public record ListRowRequest
{
    public required IReadOnlyDictionary<string, JsonElement> Values { get; init; }
}
