using System.Text.Json;
using Api.Constants;
using Api.Models;
using Api.Models.Export;
using Api.Repositories;
using Api.Security;

using static Api.Helpers.KeyHelpers;

namespace Api.Services;

public class ExportService : IExportService
{
    private readonly ISiteRepository _siteRepo;
    private readonly ISpaceRepository _spaceRepo;
    private readonly ICriteriaRepository _criteriaRepo;
    private readonly IResourceGroupRepository _resourceGroupRepo;
    private readonly ITemplateRepository _templateRepo;
    private readonly IResourceCapabilityRepository _capabilityRepo;
    private readonly IGroupCapabilityRepository _groupCapabilityRepo;
    private readonly ISchedulingRepository _schedulingRepo;
    private readonly IAvailabilityEventRepository _availabilityEventRepo;
    private readonly IRequestRepository _requestRepo;
    private readonly ICurrentTenant _currentTenant;

    public ExportService(
        ISiteRepository siteRepo,
        ISpaceRepository spaceRepo,
        ICriteriaRepository criteriaRepo,
        IResourceGroupRepository resourceGroupRepo,
        ITemplateRepository templateRepo,
        IResourceCapabilityRepository capabilityRepo,
        IGroupCapabilityRepository groupCapabilityRepo,
        ISchedulingRepository schedulingRepo,
        IAvailabilityEventRepository availabilityEventRepo,
        IRequestRepository requestRepo,
        ICurrentTenant currentTenant)
    {
        _siteRepo = siteRepo;
        _spaceRepo = spaceRepo;
        _criteriaRepo = criteriaRepo;
        _resourceGroupRepo = resourceGroupRepo;
        _templateRepo = templateRepo;
        _capabilityRepo = capabilityRepo;
        _groupCapabilityRepo = groupCapabilityRepo;
        _schedulingRepo = schedulingRepo;
        _availabilityEventRepo = availabilityEventRepo;
        _requestRepo = requestRepo;
        _currentTenant = currentTenant;
    }

    public async Task<ExportPayload> ExportAsync(ExportRequest request, CancellationToken ct = default)
    {
        const string schemaVersion = "1.0.0";

        var criteria = await _criteriaRepo.GetAllAsync();
        var criterionIdToKey = criteria.ToDictionary(c => c.Id, c => GenerateKey(c.Name));

        var groups = await _resourceGroupRepo.GetByTypeKeyAsync(ResourceTypeKeys.Space);
        var groupIdToKey = groups.ToDictionary(g => g.Id, g => GenerateKey(g.Name));

        var allSites = await _siteRepo.GetAllAsync();
        var filteredSites = request.SiteIds is { Count: > 0 }
            ? allSites.Where(s => request.SiteIds.Contains(s.Id)).ToList()
            : allSites;

        ExportData data = new();

        if (request.IncludeMasterData)
            data = await BuildMasterDataAsync(filteredSites, criteria, groups, criterionIdToKey, groupIdToKey);

        if (request.IncludePlanningData)
            data = data with { Requests = await BuildRequestDataAsync(filteredSites, criterionIdToKey) };

        return new ExportPayload
        {
            SchemaVersion = schemaVersion,
            Provenance = new ExportProvenance
            {
                ExportTimestamp = DateTime.UtcNow,
                TenantSlug = _currentTenant.TenantSlug,
                SiteIds = filteredSites.Select(s => s.Id).ToList(),
                SchemaVersion = schemaVersion
            },
            Data = data
        };
    }

    private async Task<ExportData> BuildMasterDataAsync(
        List<SiteInfo> sites,
        List<CriterionInfo> criteria,
        List<ResourceGroupInfo> groups,
        Dictionary<Guid, string> criterionIdToKey,
        Dictionary<Guid, string> groupIdToKey)
    {
        var exportCriteria = criteria
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => new ExportCriterion
            {
                Key = GenerateKey(c.Name),
                Name = c.Name,
                Description = c.Description,
                DataType = c.DataType,
                EnumValues = c.EnumValues,
                Unit = c.Unit
            }).ToList();

        // Bulk-fetch capabilities for all groups up front (was one query per group).
        var capsByGroup = await _groupCapabilityRepo.GetByGroupsAsync(groups.Select(g => g.Id).ToList());

        var exportGroups = new List<ExportSpaceGroup>();
        foreach (var g in groups.OrderBy(g => g.DisplayOrder ?? 0).ThenBy(g => g.Name, StringComparer.Ordinal))
        {
            var groupCaps = capsByGroup.GetValueOrDefault(g.Id, []);
            exportGroups.Add(new ExportSpaceGroup
            {
                Key = GenerateKey(g.Name),
                Name = g.Name,
                Description = g.Description,
                Color = g.Color,
                DisplayOrder = g.DisplayOrder ?? 0,
                Capabilities = MapCapabilities(groupCaps.Select(gc => (gc.CriterionId, gc.Value)), criterionIdToKey)
            });
        }

        var exportTemplates = await BuildTemplatesAsync(criterionIdToKey);
        var exportSites = await BuildSitesAsync(sites, groupIdToKey, criterionIdToKey);

        return new ExportData
        {
            Sites = exportSites,
            Criteria = exportCriteria,
            SpaceGroups = exportGroups,
            Templates = exportTemplates
        };
    }

    private async Task<List<ExportSite>> BuildSitesAsync(
        List<SiteInfo> sites,
        Dictionary<Guid, string> groupIdToKey,
        Dictionary<Guid, string> criterionIdToKey)
    {
        // Bulk-fetch all per-site data up front (was N+1: three queries per site plus one per space).
        var siteIds = sites.Select(s => s.Id).ToList();
        var spacesBySite = await _spaceRepo.GetBySitesAsync(siteIds);
        var settingsBySite = await _schedulingRepo.GetSettingsBySitesAsync(siteIds);
        var eventsBySite = await _availabilityEventRepo.GetBySitesAsync(siteIds);
        var capsByResource = (await _capabilityRepo.GetByResourcesAsync(
                spacesBySite.Values.SelectMany(spaces => spaces).Select(s => s.Id).ToList()))
            .GroupBy(c => c.ResourceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var exportSites = new List<ExportSite>();
        foreach (var site in sites.OrderBy(s => s.Code ?? s.Name, StringComparer.Ordinal))
        {
            var spaces = spacesBySite.GetValueOrDefault(site.Id, []);
            var exportSpaces = new List<ExportSpace>();

            foreach (var space in spaces.OrderBy(s => s.Name, StringComparer.Ordinal))
            {
                var caps = capsByResource.GetValueOrDefault(space.Id, []);
                exportSpaces.Add(new ExportSpace
                {
                    Name = space.Name,
                    Code = space.Code,
                    Description = space.Description,
                    IsPhysical = space.IsPhysical,
                    Geometry = space.Geometry,
                    Properties = space.Properties,
                    GroupKey = space.GroupId.HasValue && groupIdToKey.TryGetValue(space.GroupId.Value, out var gk) ? gk : null,
                    Capabilities = MapCapabilities(caps.Select(c => (c.CriterionId, (object?)c.Value.GetRawText())), criterionIdToKey)
                });
            }

            exportSites.Add(new ExportSite
            {
                Code = site.Code ?? GenerateKey(site.Name),
                Name = site.Name,
                Description = site.Description,
                Address = site.Address,
                SchedulingSettings = BuildSchedulingSettings(settingsBySite.GetValueOrDefault(site.Id)),
                AvailabilityEvents = BuildAvailabilityEvents(eventsBySite.GetValueOrDefault(site.Id, [])),
                Spaces = exportSpaces
            });
        }

        return exportSites;
    }

    private static ExportSchedulingSettings? BuildSchedulingSettings(SchedulingSettingsInfo? settings)
    {
        if (settings is null || settings.Id == Guid.Empty) return null;

        return new ExportSchedulingSettings
        {
            TimeZone = settings.TimeZone,
            WorkingHoursEnabled = settings.WorkingHoursEnabled,
            WorkingDayStart = settings.WorkingDayStart.ToString("HH:mm"),
            WorkingDayEnd = settings.WorkingDayEnd.ToString("HH:mm"),
            WeekendsEnabled = settings.WeekendsEnabled,
            PublicHolidaysEnabled = settings.PublicHolidaysEnabled,
            PublicHolidayRegion = settings.PublicHolidayRegion
        };
    }

    private static List<ExportAvailabilityEvent>? BuildAvailabilityEvents(List<AvailabilityEventInfo> events)
    {
        if (events.Count == 0) return null;

        return events
            .OrderBy(e => e.StartTs)
            .Select(e => new ExportAvailabilityEvent
            {
                Title = e.Title,
                Description = e.Description,
                EventType = e.EventType,
                DefaultEffect = e.DefaultEffect,
                StartTs = e.StartTs,
                EndTs = e.EndTs,
                IsRecurring = e.IsRecurring,
                RecurrenceRule = e.RecurrenceRule,
                Enabled = e.Enabled
            }).ToList();
    }

    private async Task<List<ExportTemplate>> BuildTemplatesAsync(Dictionary<Guid, string> criterionIdToKey)
    {
        var templatesByType = new List<(string EntityType, List<Template> Templates)>();
        foreach (var entityType in new[] { TemplateEntityTypes.Space, TemplateEntityTypes.Group, TemplateEntityTypes.Request })
            templatesByType.Add((entityType, await _templateRepo.GetAllAsync(entityType)));

        // Bulk-fetch items for all templates in one query (was one query per template).
        var itemsByTemplate = await _templateRepo.GetTemplateItemsByTemplatesAsync(
            templatesByType.SelectMany(t => t.Templates).Select(t => t.Id).ToList());

        var allTemplates = new List<ExportTemplate>();
        foreach (var (entityType, templates) in templatesByType)
        {
            foreach (var template in templates.OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                var items = itemsByTemplate.GetValueOrDefault(template.Id, []);
                allTemplates.Add(new ExportTemplate
                {
                    Key = GenerateKey(template.Name),
                    Name = template.Name,
                    Description = template.Description,
                    EntityType = entityType,
                    DurationValue = template.DurationValue,
                    DurationUnit = template.DurationUnit,
                    FixedStart = template.FixedStart,
                    FixedEnd = template.FixedEnd,
                    FixedDuration = template.FixedDuration,
                    Items = items
                        .Where(i => criterionIdToKey.ContainsKey(i.CriterionId))
                        .OrderBy(i => criterionIdToKey[i.CriterionId], StringComparer.Ordinal)
                        .Select(i => new ExportTemplateItem
                        {
                            CriterionKey = criterionIdToKey[i.CriterionId],
                            Value = i.Value
                        }).ToList()
                });
            }
        }
        return allTemplates;
    }

    private async Task<List<ExportRequestData>> BuildRequestDataAsync(List<SiteInfo> sites, Dictionary<Guid, string> criterionIdToKey)
    {
        var resourceIdToName = new Dictionary<Guid, string>();
        var resourceIdToSiteCode = new Dictionary<Guid, string>();
        var allowedResourceIds = new HashSet<Guid>();

        // Bulk-fetch spaces for all sites in one query (was one query per site).
        var spacesBySite = await _spaceRepo.GetBySitesAsync(sites.Select(s => s.Id).ToList());

        foreach (var site in sites)
        {
            var siteCode = site.Code ?? GenerateKey(site.Name);
            foreach (var space in spacesBySite.GetValueOrDefault(site.Id, []))
            {
                allowedResourceIds.Add(space.Id);
                resourceIdToName[space.Id] = space.Name;
                resourceIdToSiteCode[space.Id] = siteCode;
            }
        }

        var allRequests = await _requestRepo.GetAllAsync(includeRequirements: true);

        return allRequests
            .Select(r => (Request: r, SpaceResourceId: r.GetSpaceResourceId()))
            .Where(x => x.SpaceResourceId is { } id && allowedResourceIds.Contains(id))
            .OrderBy(x => x.Request.Name, StringComparer.Ordinal)
            .Select(x =>
            {
                var r = x.Request;
                var spaceId = x.SpaceResourceId!.Value;
                return new ExportRequestData
                {
                    Name = r.Name,
                    Description = r.Description,
                    ResourceName = resourceIdToName.TryGetValue(spaceId, out var sn) ? sn : null,
                    SiteCode = resourceIdToSiteCode.TryGetValue(spaceId, out var sc) ? sc : null,
                    RequestItemId = r.RequestItemId,
                    StartTs = r.StartTs,
                    EndTs = r.EndTs,
                    EarliestStartTs = r.EarliestStartTs,
                    LatestEndTs = r.LatestEndTs,
                    MinimalDurationValue = r.MinimalDurationValue,
                    MinimalDurationUnit = r.MinimalDurationUnit,
                    ActualDurationValue = r.ActualDurationValue,
                    ActualDurationUnit = r.ActualDurationUnit,
                    Status = r.Status,
                    SchedulingSettingsApply = r.SchedulingSettingsApply,
                    Requirements = MapCapabilities(r.Requirements?.Select(rq => (rq.CriterionId, (object?)rq.Value)) ?? [], criterionIdToKey),
                };
            }).ToList();
    }

    private static List<ExportCapability>? MapCapabilities(
        IEnumerable<(Guid CriterionId, object? Value)> capabilities,
        Dictionary<Guid, string> criterionIdToKey)
    {
        var list = capabilities
            .Where(c => criterionIdToKey.ContainsKey(c.CriterionId))
            .OrderBy(c => criterionIdToKey[c.CriterionId], StringComparer.Ordinal)
            .Select(c => new ExportCapability
            {
                CriterionKey = criterionIdToKey[c.CriterionId],
                Value = c.Value is JsonElement je ? je : JsonSerializer.SerializeToElement(c.Value)
            }).ToList();

        return list.Count > 0 ? list : null;
    }
}
