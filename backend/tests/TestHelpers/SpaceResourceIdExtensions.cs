using Api.Models;

namespace Api.Tests.TestHelpers;

/// <summary>
/// Test helper extensions for working with RequestInfo and space resource IDs.
/// Used in tests to verify space assignment behavior.
/// </summary>
public static class SpaceResourceIdExtensions
{
    /// <summary>
    /// Gets the space resource ID from a request's assignments.
    /// Returns null if no space assignment exists or if it's cancelled.
    /// </summary>
    public static Guid? SpaceResourceId(this RequestInfo r)
    {
        var spaceAssignment = r.Assignments.FirstOrDefault(
            a => a.ResourceTypeKey == ResourceTypeKeys.Space && a.AssignmentStatus != AssignmentStatuses.Cancelled
        );
        return spaceAssignment?.ResourceId;
    }

    /// <summary>
    /// Gets all non-cancelled assignments of a specific resource type.
    /// </summary>
    public static IReadOnlyList<ResourceAssignmentInfo> GetAssignmentsByType(
        this RequestInfo r,
        string resourceTypeKey
    )
    {
        return r.Assignments
            .Where(a => a.ResourceTypeKey == resourceTypeKey && a.AssignmentStatus != AssignmentStatuses.Cancelled)
            .ToList();
    }

    /// <summary>
    /// Checks if a request has a space assignment (non-cancelled).
    /// </summary>
    public static bool HasSpaceAssignment(this RequestInfo r)
    {
        return r.Assignments.Any(
            a => a.ResourceTypeKey == ResourceTypeKeys.Space && a.AssignmentStatus != AssignmentStatuses.Cancelled
        );
    }

    /// <summary>
    /// Gets the first non-cancelled assignment of any type.
    /// </summary>
    public static ResourceAssignmentInfo? FirstAssignment(this RequestInfo r)
    {
        return r.Assignments.FirstOrDefault(a => a.AssignmentStatus != AssignmentStatuses.Cancelled);
    }
}
