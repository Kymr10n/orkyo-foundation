using System.Text.Json;
using Api.Models;
using Api.Models.Export;

namespace Orkyo.Foundation.Tests.Models;

/// <summary>
/// Covers the 0%-coverage Export model types in <c>Models/Export/ExportPayload.cs</c>.
/// </summary>
public class ExportModelsTests
{
    // ── ExportRequest ──────────────────────────────────────────────────────

    [Fact]
    public void ExportRequest_Defaults_IncludeMasterDataTrue_PlanningDataFalse()
    {
        var req = new ExportRequest();

        req.IncludeMasterData.Should().BeTrue();
        req.IncludePlanningData.Should().BeFalse();
        req.SiteIds.Should().BeNull();
    }

    [Fact]
    public void ExportRequest_StoresOptionalSiteIds()
    {
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var req = new ExportRequest { SiteIds = ids, IncludePlanningData = true };

        req.SiteIds.Should().BeEquivalentTo(ids);
        req.IncludePlanningData.Should().BeTrue();
    }

    // ── ExportPayload / ExportProvenance / ExportData ──────────────────────

    [Fact]
    public void ExportPayload_StoresAllTopLevelFields()
    {
        var ts = DateTime.UtcNow;
        var payload = new ExportPayload
        {
            SchemaVersion = "1.0.0",
            Provenance = new ExportProvenance
            {
                ExportTimestamp = ts,
                TenantSlug = "acme",
                SchemaVersion = "1.0.0"
            },
            Data = new ExportData()
        };

        payload.SchemaVersion.Should().Be("1.0.0");
        payload.Provenance.TenantSlug.Should().Be("acme");
        payload.Provenance.ExportTimestamp.Should().Be(ts);
        payload.Data.Should().NotBeNull();
    }

    [Fact]
    public void ExportData_AllCollections_NullByDefault()
    {
        var data = new ExportData();

        data.Sites.Should().BeNull();
        data.Criteria.Should().BeNull();
        data.SpaceGroups.Should().BeNull();
        data.Templates.Should().BeNull();
        data.Requests.Should().BeNull();
    }

    // ── ExportSite / ExportSpace / ExportCapability ────────────────────────

    [Fact]
    public void ExportSite_StoresSpacesAndOptionalFields()
    {
        var site = new ExportSite
        {
            Code = "HQ",
            Name = "Headquarters",
            Description = "Main office",
            Address = "123 Main St",
            SchedulingSettings = new ExportSchedulingSettings
            {
                TimeZone = "Europe/Zurich",
                WorkingHoursEnabled = true,
                WorkingDayStart = "08:00",
                WorkingDayEnd = "17:00",
                WeekendsEnabled = false,
                PublicHolidaysEnabled = true,
                PublicHolidayRegion = "CH"
            },
            Spaces = new List<ExportSpace>
            {
                new()
                {
                    Name = "Room A",
                    Code = "RA-1",
                    IsPhysical = true,
                    Capabilities = new List<ExportCapability>
                    {
                        new() { CriterionKey = "capacity", Value = JsonDocument.Parse("10").RootElement }
                    }
                }
            }
        };

        site.Code.Should().Be("HQ");
        site.Spaces.Should().HaveCount(1);
        site.Spaces[0].Capabilities.Should().HaveCount(1);
        site.Spaces[0].Capabilities![0].CriterionKey.Should().Be("capacity");
        site.SchedulingSettings!.TimeZone.Should().Be("Europe/Zurich");
    }

    [Fact]
    public void ExportSpace_Defaults_EmptyLists()
    {
        var space = new ExportSpace { Name = "Hall A", IsPhysical = true };

        space.Capabilities.Should().BeNull();
        space.Geometry.Should().BeNull();
        space.Properties.Should().BeNull();
        space.GroupKey.Should().BeNull();
    }

    // ── ExportCriterion ────────────────────────────────────────────────────

    [Fact]
    public void ExportCriterion_StoresAllFields()
    {
        var criterion = new ExportCriterion
        {
            Key = "shift-model",
            Name = "Shift Model",
            Description = "Work shift pattern",
            DataType = CriterionDataType.Enum,
            EnumValues = new List<string> { "2-shift", "3-shift" },
            Unit = null
        };

        criterion.Key.Should().Be("shift-model");
        criterion.DataType.Should().Be(CriterionDataType.Enum);
        criterion.EnumValues.Should().HaveCount(2);
    }

    // ── ExportSpaceGroup ───────────────────────────────────────────────────

    [Fact]
    public void ExportSpaceGroup_StoresAllFields()
    {
        var group = new ExportSpaceGroup
        {
            Key = "hall-a",
            Name = "Hall A",
            Description = "Production hall",
            Color = "#3b82f6",
            DisplayOrder = 1
        };

        group.Key.Should().Be("hall-a");
        group.Color.Should().Be("#3b82f6");
        group.DisplayOrder.Should().Be(1);
    }

    // ── ExportTemplate / ExportTemplateItem ───────────────────────────────

    [Fact]
    public void ExportTemplate_StoresItemsAndFields()
    {
        var template = new ExportTemplate
        {
            Key = "work-order",
            Name = "Work Order",
            Description = "Standard production request",
            EntityType = "request",
            DurationValue = 5,
            DurationUnit = "days",
            FixedDuration = true,
            Items = new List<ExportTemplateItem>
            {
                new() { CriterionKey = "shift-model", Value = "\"2-shift\"" }
            }
        };

        template.Key.Should().Be("work-order");
        template.DurationValue.Should().Be(5);
        template.DurationUnit.Should().Be("days");
        template.FixedDuration.Should().BeTrue();
        template.Items.Should().HaveCount(1);
    }

    [Fact]
    public void ExportTemplate_Defaults_FixedFlagsAreFalse()
    {
        var template = new ExportTemplate
        {
            Key = "t",
            Name = "Test",
            EntityType = "space"
        };

        template.FixedStart.Should().BeFalse();
        template.FixedEnd.Should().BeFalse();
        template.FixedDuration.Should().BeFalse();
        template.Items.Should().BeEmpty();
    }

    // ── ExportAvailabilityEvent ────────────────────────────────────────────

    [Fact]
    public void ExportAvailabilityEvent_StoresAllFields()
    {
        var start = new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 12, 26, 0, 0, 0, DateTimeKind.Utc);

        var ev = new ExportAvailabilityEvent
        {
            Title = "Christmas",
            EventType = AvailabilityEventType.PublicHoliday,
            DefaultEffect = DefaultEffect.Closed,
            StartTs = start,
            EndTs = end,
            IsRecurring = false,
            Enabled = true
        };

        ev.Title.Should().Be("Christmas");
        ev.EventType.Should().Be(AvailabilityEventType.PublicHoliday);
        ev.DefaultEffect.Should().Be(DefaultEffect.Closed);
        ev.StartTs.Should().Be(start);
    }

    // ── ExportSchedulingSettings ───────────────────────────────────────────

    [Fact]
    public void ExportSchedulingSettings_StoresAllFields()
    {
        var settings = new ExportSchedulingSettings
        {
            TimeZone = "UTC",
            WorkingHoursEnabled = false,
            WorkingDayStart = "08:00",
            WorkingDayEnd = "17:00",
            WeekendsEnabled = true,
            PublicHolidaysEnabled = false,
            PublicHolidayRegion = null
        };

        settings.TimeZone.Should().Be("UTC");
        settings.WeekendsEnabled.Should().BeTrue();
    }

    // ── ExportRequestData ──────────────────────────────────────────────────

    [Fact]
    public void ExportRequestData_StoresAllFields()
    {
        var data = new ExportRequestData
        {
            Name = "Install HVAC",
            Description = "Install new HVAC unit",
            ResourceName = "Roof",
            SiteCode = "HQ",
            MinimalDurationValue = 2,
            MinimalDurationUnit = DurationUnit.Days,
            Status = RequestStatus.Planned,
            SchedulingSettingsApply = true
        };

        data.Name.Should().Be("Install HVAC");
        data.MinimalDurationValue.Should().Be(2);
        data.Status.Should().Be(RequestStatus.Planned);
        data.SchedulingSettingsApply.Should().BeTrue();
    }
}
