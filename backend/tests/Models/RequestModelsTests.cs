using System.Text.Json;
using Api.Models;

namespace Orkyo.Foundation.Tests.Models;

/// <summary>
/// Covers <c>Models/Request.cs</c>: RequestInfo.IsScheduled, RequestRequirementInfo.AppliesTo, and the
/// defaults of RequestRequirementInfo, CriterionBasicInfo, ScheduleRequestRequest and MoveRequestRequest.
/// </summary>
public class RequestModelsTests
{
    // ── RequestInfo.IsScheduled ────────────────────────────────────────────

    [Fact]
    public void RequestInfo_IsScheduled_TrueWhenSpaceAndTimestampsSet()
    {
        var spaceAssignment = new ResourceAssignmentInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            ResourceId = Guid.NewGuid(),
            ResourceTypeKey = ResourceTypeKeys.Space,
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow.AddDays(1),
            AssignmentStatus = AssignmentStatuses.Planned,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var info = BuildRequest(start: DateTime.UtcNow, end: DateTime.UtcNow.AddDays(1), assignments: new[] { spaceAssignment });

        info.IsScheduled.Should().BeTrue();
    }

    [Fact]
    public void RequestInfo_IsScheduled_FalseWhenNoSpaceAssignment()
    {
        var info = BuildRequest(start: DateTime.UtcNow, end: DateTime.UtcNow.AddDays(1));

        info.IsScheduled.Should().BeFalse();
    }

    [Fact]
    public void RequestInfo_IsScheduled_FalseWhenStartTsNull()
    {
        var spaceAssignment = new ResourceAssignmentInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            ResourceId = Guid.NewGuid(),
            ResourceTypeKey = ResourceTypeKeys.Space,
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow.AddDays(1),
            AssignmentStatus = AssignmentStatuses.Planned,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var info = BuildRequest(start: null, end: DateTime.UtcNow.AddDays(1), assignments: new[] { spaceAssignment });

        info.IsScheduled.Should().BeFalse();
    }

    [Fact]
    public void RequestInfo_IsScheduled_FalseWhenEndTsNull()
    {
        var spaceAssignment = new ResourceAssignmentInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            ResourceId = Guid.NewGuid(),
            ResourceTypeKey = ResourceTypeKeys.Space,
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow.AddDays(1),
            AssignmentStatus = AssignmentStatuses.Planned,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var info = BuildRequest(start: DateTime.UtcNow, end: null, assignments: new[] { spaceAssignment });

        info.IsScheduled.Should().BeFalse();
    }

    [Fact]
    public void RequestInfo_IsScheduled_FalseWhenAllNull()
    {
        var info = BuildRequest(start: null, end: null);

        info.IsScheduled.Should().BeFalse();
    }

    // ── RequestRequirementInfo ─────────────────────────────────────────────

    [Fact]
    public void RequestRequirementInfo_Criterion_IsOptional()
    {
        var req = new RequestRequirementInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            CriterionId = Guid.NewGuid(),
            Value = JsonDocument.Parse("\"2-shift\"").RootElement
        };

        req.Criterion.Should().BeNull();
    }

    // ── CriterionBasicInfo ─────────────────────────────────────────────────

    [Fact]
    public void CriterionBasicInfo_ResourceTypeKeys_DefaultsToEmpty()
    {
        var info = new CriterionBasicInfo { Id = Guid.NewGuid(), Name = "Crane", DataType = CriterionDataType.Boolean };

        info.ResourceTypeKeys.Should().BeEmpty();
    }

    // ── RequestRequirementInfo.AppliesTo ──────────────────────────────────

    [Theory]
    [InlineData(null, "mill", true)]                 // no criterion joined: applies to every type
    [InlineData(new string[0], "mill", true)]        // no scope recorded: applies to every type
    [InlineData(new[] { "mill" }, "mill", true)]
    [InlineData(new[] { "mill" }, "person", false)]
    [InlineData(new[] { "mill", "person" }, "person", true)]
    [InlineData(new[] { "mill" }, "Mill", false)]    // ordinal, like the analyzer and the builder
    public void RequestRequirementInfo_AppliesTo_FollowsTheCriterionScope(string[]? scope, string typeKey, bool expected)
    {
        var criterionId = Guid.NewGuid();
        var req = new RequestRequirementInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            CriterionId = criterionId,
            Value = JsonDocument.Parse("true").RootElement,
            Criterion = scope is null ? null : new CriterionBasicInfo
            {
                Id = criterionId,
                Name = "Tolerance",
                DataType = CriterionDataType.Boolean,
                ResourceTypeKeys = scope,
            },
        };

        req.AppliesTo(typeKey).Should().Be(expected);
    }

    // ── ScheduleRequestRequest ─────────────────────────────────────────────

    [Fact]
    public void ScheduleRequestRequest_AllNullByDefault()
    {
        var req = new ScheduleRequestRequest();

        req.ResourceId.Should().BeNull();
        req.StartTs.Should().BeNull();
        req.EndTs.Should().BeNull();
        req.ActualDurationValue.Should().BeNull();
        req.ActualDurationUnit.Should().BeNull();
    }

    // ── MoveRequestRequest ─────────────────────────────────────────────────

    [Fact]
    public void MoveRequestRequest_AllowsNullParent()
    {
        var req = new MoveRequestRequest { SortOrder = 0 };

        req.NewParentRequestId.Should().BeNull();
        req.SortOrder.Should().Be(0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static RequestInfo BuildRequest(DateTime? start, DateTime? end, ResourceAssignmentInfo[]? assignments = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Request",
        PlanningMode = PlanningMode.Leaf,
        MinimalDurationValue = 1,
        MinimalDurationUnit = DurationUnit.Days,
        Status = RequestStatus.New,
        SchedulingSettingsApply = true,
        Assignments = assignments ?? Array.Empty<ResourceAssignmentInfo>(),
        TargetResourceTypeKeys = [ResourceTypeKeys.Space],
        StartTs = start,
        EndTs = end
    };
}
