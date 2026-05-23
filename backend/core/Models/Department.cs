namespace Api.Models;

/// <summary>
/// Tenant-scoped reference data: a hierarchical department tree. Departments are
/// purely organizational — they do not carry capabilities, availability, or any
/// scheduling/allocation behavior. Persons reference a department via
/// <see cref="PersonProfileInfo.DepartmentId"/>.
/// </summary>
public record DepartmentInfo
{
    public required Guid Id { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public required string Name { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Nested representation for tree rendering. Children are populated recursively;
/// a leaf has an empty <see cref="Children"/> list.
/// </summary>
public record DepartmentTreeNode
{
    public required Guid Id { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public required string Name { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public required List<DepartmentTreeNode> Children { get; init; }
}

public record CreateDepartmentRequest
{
    public required string Name { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
}

public record UpdateDepartmentRequest
{
    public string? Name { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public bool? IsActive { get; init; }
    /// <summary>
    /// Distinguishes "do not change parent" from "set parent to NULL (root)".
    /// When true, ParentDepartmentId is honored even if null. When false,
    /// the existing parent is preserved.
    /// </summary>
    public bool ChangeParent { get; init; }
}
