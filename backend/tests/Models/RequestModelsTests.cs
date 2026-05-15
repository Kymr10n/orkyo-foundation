using System.Text.Json;
using Api.Models;

namespace Orkyo.Foundation.Tests.Models;

/// <summary>
/// Covers uncovered types in <c>Models/Request.cs</c>: RequestInfo.IsScheduled, RequestRequirementInfo,
/// CriterionBasicInfo, ScheduleRequestRequest, MoveRequestRequest, DeleteSubtreeResponse, and enums.
/// </summary>
public class RequestModelsTests
{
    // ── DurationUnit, PlanningMode, RequestStatus enums ───────────────────

    [Theory]
    [InlineData(DurationUnit.Minutes)]
    [InlineData(DurationUnit.Hours)]
    [InlineData(DurationUnit.Days)]
    [InlineData(DurationUnit.Weeks)]
    [InlineData(DurationUnit.Months)]
    [InlineData(DurationUnit.Years)]
    public void DurationUnit_AllValues_AreDefined(DurationUnit unit)
    {
        Enum.IsDefined(unit).Should().BeTrue();
    }

    [Theory]
    [InlineData(PlanningMode.Leaf)]
    [InlineData(PlanningMode.Summary)]
    [InlineData(PlanningMode.Container)]
    public void PlanningMode_AllValues_AreDefined(PlanningMode mode)
    {
        Enum.IsDefined(mode).Should().BeTrue();
    }

    [Theory]
    [InlineData(RequestStatus.Planned)]
    [InlineData(RequestStatus.InProgress)]
    [InlineData(RequestStatus.Done)]
    [InlineData(RequestStatus.Cancelled)]
    public void RequestStatus_AllValues_AreDefined(RequestStatus status)
    {
        Enum.IsDefined(status).Should().BeTrue();
    }

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
    public void RequestRequirementInfo_StoresAllFields()
    {
        var id = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var value = JsonDocument.Parse("42").RootElement;

        var criterion = new CriterionBasicInfo
        {
            Id = criterionId,
            Name = "Capacity",
            DataType = CriterionDataType.Number,
            Unit = "pax",
            EnumValues = null
        };

        var req = new RequestRequirementInfo
        {
            Id = id,
            RequestId = requestId,
            CriterionId = criterionId,
            Value = value,
            CreatedAt = now,
            Criterion = criterion
        };

        req.Id.Should().Be(id);
        req.RequestId.Should().Be(requestId);
        req.CriterionId.Should().Be(criterionId);
        req.Value.GetInt32().Should().Be(42);
        req.Criterion!.Name.Should().Be("Capacity");
    }

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
    public void CriterionBasicInfo_StoresAllFields()
    {
        var id = Guid.NewGuid();

        var info = new CriterionBasicInfo
        {
            Id = id,
            Name = "Shift Model",
            DataType = CriterionDataType.Enum,
            Unit = null,
            EnumValues = new List<string> { "2-shift", "3-shift" }
        };

        info.Id.Should().Be(id);
        info.Name.Should().Be("Shift Model");
        info.DataType.Should().Be(CriterionDataType.Enum);
        info.EnumValues.Should().HaveCount(2);
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

    [Fact]
    public void ScheduleRequestRequest_StoresSchedulingFields()
    {
        var resourceId = Guid.NewGuid();
        var start = new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 7, 17, 0, 0, DateTimeKind.Utc);

        var req = new ScheduleRequestRequest
        {
            ResourceId = resourceId,
            StartTs = start,
            EndTs = end,
            ActualDurationValue = 2,
            ActualDurationUnit = DurationUnit.Days
        };

        req.ResourceId.Should().Be(resourceId);
        req.StartTs.Should().Be(start);
        req.ActualDurationValue.Should().Be(2);
        req.ActualDurationUnit.Should().Be(DurationUnit.Days);
    }

    // ── MoveRequestRequest ─────────────────────────────────────────────────

    [Fact]
    public void MoveRequestRequest_AllowsNullParent()
    {
        var req = new MoveRequestRequest { SortOrder = 0 };

        req.NewParentRequestId.Should().BeNull();
        req.SortOrder.Should().Be(0);
    }

    [Fact]
    public void MoveRequestRequest_StoresParentAndOrder()
    {
        var parentId = Guid.NewGuid();
        var req = new MoveRequestRequest { NewParentRequestId = parentId, SortOrder = 3 };

        req.NewParentRequestId.Should().Be(parentId);
        req.SortOrder.Should().Be(3);
    }

    // ── DeleteSubtreeResponse ──────────────────────────────────────────────

    [Fact]
    public void DeleteSubtreeResponse_StoresDeletedCount()
    {
        var response = new DeleteSubtreeResponse { DeletedCount = 7 };

        response.DeletedCount.Should().Be(7);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static RequestInfo BuildRequest(DateTime? start, DateTime? end, ResourceAssignmentInfo[]? assignments = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Request",
        PlanningMode = PlanningMode.Leaf,
        MinimalDurationValue = 1,
        MinimalDurationUnit = DurationUnit.Days,
        Status = RequestStatus.Planned,
        SchedulingSettingsApply = true,
        Assignments = assignments ?? Array.Empty<ResourceAssignmentInfo>(),
        StartTs = start,
        EndTs = end
    };
}
