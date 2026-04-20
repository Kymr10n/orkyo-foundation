namespace Api.Services;

/// <summary>
/// Domain-facing workspace context. Contains only the information domain code needs:
/// which org am I operating on, and where is its data?
/// </summary>
public sealed class OrgContext
{
    public required Guid OrgId { get; init; }
    public required string OrgSlug { get; init; }
    public required string DbConnectionString { get; init; }
}

/// <summary>
/// Accessor interface for retrieving the current <see cref="OrgContext"/>.
/// </summary>
public interface IOrgContextAccessor
{
    OrgContext Current { get; }
}
