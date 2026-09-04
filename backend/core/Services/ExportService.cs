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
    private readonly IResourceRepository _resourceRepo;
    private readonly IResourceTypeRepository _resourceTypeRepo;
    private readonly ICriteriaRepository _criteriaRepo;
    private readonly IResourceGroupRepository _resourceGroupRepo;
    private readonly ITemplateRepository _templateRepo;
    private readonly IResourceCapabilityRepository _capabilityRepo;
    private readonly IGroupCapabilityRepository _groupCapabilityRepo;
    private readonly ISchedulingRepository _schedulingRepo;
    private readonly IAvailabilityEventRepository _availabilityEventRepo;
    private readonly IRequestRepository _requestRepo;
    private readonly IListDefinitionRepository _listDefinitionRepo;
    private readonly IListInstanceRepository _listInstanceRepo;
    private readonly ICurrentTenant _currentTenant;

    public ExportService(
        ISiteRepository siteRepo,
        IResourceRepository resourceRepo,
        IResourceTypeRepository resourceTypeRepo,
        ICriteriaRepository criteriaRepo,
        IResourceGroupRepository resourceGroupRepo,
        ITemplateRepository templateRepo,
        IResourceCapabilityRepository capabilityRepo,
        IGroupCapabilityRepository groupCapabilityRepo,
        ISchedulingRepository schedulingRepo,
        IAvailabilityEventRepository availabilityEventRepo,
        IRequestRepository requestRepo,
        IListDefinitionRepository listDefinitionRepo,
        IListInstanceRepository listInstanceRepo,
        ICurrentTenant currentTenant)
    {
        _siteRepo = siteRepo;
        _resourceRepo = resourceRepo;
        _resourceTypeRepo = resourceTypeRepo;
        _criteriaRepo = criteriaRepo;
        _resourceGroupRepo = resourceGroupRepo;
        _templateRepo = templateRepo;
        _capabilityRepo = capabilityRepo;
        _groupCapabilityRepo = groupCapabilityRepo;
        _schedulingRepo = schedulingRepo;
        _availabilityEventRepo = availabilityEventRepo;
        _requestRepo = requestRepo;
        _listDefinitionRepo = listDefinitionRepo;
        _listInstanceRepo = listInstanceRepo;
        _currentTenant = currentTenant;
    }

    public async Task<ExportPayload> ExportAsync(ExportRequest request, CancellationToken ct = default)
    {
        const string schemaVersion = "1.0.0";

        var criteria = await _criteriaRepo.GetAllAsync(ct);
        var criterionIdToKey = criteria.ToDictionary(c => c.Id, c => GenerateKey(c.Name));

        // Groups of every placeable type. The resources below come from GetPlaceableBySitesAsync
        // (has_geometry), and a group section keyed to space alone disagreed with them the moment
        // a second placeable type existed — machines in the export, their cells silently missing.
        var placeableKeys = await _resourceTypeRepo.GetPlaceableKeysAsync(ct);
        var groups = await _resourceGroupRepo.GetByTypeKeysAsync(placeableKeys, ct);
        var groupIdToKey = groups.ToDictionary(g => g.Id, g => GenerateKey(g.Name));

        var allSites = await _siteRepo.GetAllAsync(ct);
        var filteredSites = request.SiteIds is { Count: > 0 }
            ? allSites.Where(s => request.SiteIds.Contains(s.Id)).ToList()
            : allSites;

        ExportData data = new();

        if (request.IncludeMasterData)
            data = await BuildMasterDataAsync(filteredSites, criteria, groups, criterionIdToKey, groupIdToKey, ct);

        if (request.IncludePlanningData)
            data = data with { Requests = await BuildRequestDataAsync(filteredSites, criterionIdToKey, ct) };

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
        Dictionary<Guid, string> groupIdToKey,
        CancellationToken ct)
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
        var capsByGroup = await _groupCapabilityRepo.GetByGroupsAsync(groups.Select(g => g.Id).ToList(), ct);

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

        var exportTemplates = await BuildTemplatesAsync(criterionIdToKey, ct);
        var exportSites = await BuildSitesAsync(sites, groupIdToKey, criterionIdToKey, ct);
        var exportResources = await BuildResourcesAsync(sites, criterionIdToKey, ct);
        var exportListDefinitions = await BuildListDefinitionsAsync(ct);

        return new ExportData
        {
            Sites = exportSites,
            Criteria = exportCriteria,
            SpaceGroups = exportGroups,
            Templates = exportTemplates,
            Resources = exportResources,
            ListDefinitions = exportListDefinitions
        };
    }

    /// <summary>
    /// Every list definition with its columns, and the shared instances built from it with their
    /// rows.
    ///
    /// Inactive definitions are included: a retired shape still describes data that exists, and
    /// an export that dropped it would lose the meaning of the rows it exported. Per-resource
    /// instances are deliberately absent — see ExportData.ListDefinitions.
    /// </summary>
    private async Task<List<ExportListDefinition>> BuildListDefinitionsAsync(CancellationToken ct)
    {
        // Four round trips for the whole section, however many definitions exist: definitions,
        // columns, instances, rows. The grouped reads keep each group's order (form order for
        // columns, name for instances, insertion for rows), so the payload is unchanged.
        var definitions = await _listDefinitionRepo.GetAllAsync(includeInactive: true, ct: ct);
        var definitionIds = definitions.Select(d => d.Id).ToList();
        var columnsByDefinition = await _listDefinitionRepo.GetColumnsByDefinitionsAsync(definitionIds, ct);
        var instancesByDefinition = await _listInstanceRepo.GetSharedByDefinitionsAsync(definitionIds, ct);
        var rowsByInstance = await _listInstanceRepo.GetRowsByInstancesAsync(
            instancesByDefinition.Values.SelectMany(i => i).Select(i => i.Id).ToList(), ct);

        var exported = new List<ExportListDefinition>();
        foreach (var definition in definitions)
        {
            var columns = columnsByDefinition.GetValueOrDefault(definition.Id, []);
            var instances = instancesByDefinition.GetValueOrDefault(definition.Id, []);

            var exportedInstances = new List<ExportListInstance>();
            foreach (var instance in instances)
            {
                var rows = rowsByInstance.GetValueOrDefault(instance.Id, []);
                exportedInstances.Add(new ExportListInstance
                {
                    Name = instance.Name!,
                    Rows = rows.Select(row => new ExportListRow
                    {
                        Id = row.Id,
                        Values = new Dictionary<string, JsonElement>(row.Values),
                    }).ToList(),
                });
            }

            exported.Add(new ExportListDefinition
            {
                Name = definition.Name,
                Description = definition.Description,
                Scope = definition.Scope,
                DisplayColumnKey = columns.FirstOrDefault(c => c.Id == definition.DisplayColumnId)?.Key,
                IsActive = definition.IsActive,
                Columns = columns.Select(column => new ExportListColumn
                {
                    Key = column.Key,
                    Label = column.Label,
                    Description = column.Description,
                    DataType = column.DataType,
                    Options = column.Options?.ToList(),
                    IsRequired = column.IsRequired,
                    SortOrder = column.SortOrder,
                    IsActive = column.IsActive,
                }).ToList(),
                SharedInstances = exportedInstances,
            });
        }

        return exported;
    }

    /// <summary>
    /// Every active resource of a non-placeable type — people, tools and whatever
    /// a tenant defined for itself. Placeable ones are exported under their site
    /// (BuildSitesAsync); before this existed the payload simply omitted the rest,
    /// so an "export my data" of a tools-and-people tenant returned neither.
    /// </summary>
    private async Task<List<ExportResource>> BuildResourcesAsync(
        List<SiteInfo> sites,
        Dictionary<Guid, string> criterionIdToKey,
        CancellationToken ct)
    {
        var types = await _resourceTypeRepo.GetAllAsync(ct);
        var nonPlaceableKeys = types
            .Where(t => !t.HasGeometry)
            .Select(t => t.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (nonPlaceableKeys.Count == 0) return [];

        var resources = (await _resourceRepo.GetEveryAsync(new ResourceListFilter { IsActive = true }, ct))
            .Where(r => nonPlaceableKeys.Contains(r.ResourceTypeKey))
            .ToList();
        if (resources.Count == 0) return [];

        // Sites are referenced by code, not id: an id is meaningless in another
        // deployment, which is where an export tends to be read.
        var siteCodeById = sites.ToDictionary(s => s.Id, s => s.Code ?? GenerateKey(s.Name));
        var capsByResource = (await _capabilityRepo.GetByResourcesAsync(resources.Select(r => r.Id).ToList(), ct))
            .GroupBy(c => c.ResourceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return resources
            .OrderBy(r => r.ResourceTypeKey, StringComparer.Ordinal)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => new ExportResource
            {
                ResourceTypeKey = r.ResourceTypeKey,
                Name = r.Name,
                Description = r.Description,
                ExternalReference = r.ExternalReference,
                AllocationMode = r.AllocationMode,
                BaseAvailabilityPercent = r.BaseAvailabilityPercent,
                CrossSiteAllowed = r.CrossSiteAllowed,
                HomeSiteCode = r.HomeSiteId.HasValue && siteCodeById.TryGetValue(r.HomeSiteId.Value, out var code)
                    ? code
                    : null,
                Metadata = r.Properties,
                CustomFields = r.CustomFields,
                Capabilities = MapCapabilities(
                    capsByResource.GetValueOrDefault(r.Id, []).Select(c => (c.CriterionId, (object?)c.Value.GetRawText())),
                    criterionIdToKey)
            })
            .ToList();
    }

    private async Task<List<ExportSite>> BuildSitesAsync(
        List<SiteInfo> sites,
        Dictionary<Guid, string> groupIdToKey,
        Dictionary<Guid, string> criterionIdToKey,
        CancellationToken ct)
    {
        // Bulk-fetch all per-site data up front (was N+1: three queries per site plus one per space).
        var siteIds = sites.Select(s => s.Id).ToList();
        var spacesBySite = await _resourceRepo.GetPlaceableBySitesAsync(siteIds, ct);
        var settingsBySite = await _schedulingRepo.GetSettingsBySitesAsync(siteIds, ct);
        var eventsBySite = await _availabilityEventRepo.GetBySitesAsync(siteIds, ct);
        var capsByResource = (await _capabilityRepo.GetByResourcesAsync(
                spacesBySite.Values.SelectMany(spaces => spaces).Select(s => s.Id).ToList(), ct))
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
                    CustomFields = space.CustomFields,
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

    private async Task<List<ExportTemplate>> BuildTemplatesAsync(
        Dictionary<Guid, string> criterionIdToKey, CancellationToken ct)
    {
        var templatesByType = new List<(string EntityType, List<Template> Templates)>();
        foreach (var entityType in new[] { TemplateEntityTypes.Space, TemplateEntityTypes.Group, TemplateEntityTypes.Request })
            templatesByType.Add((entityType, await _templateRepo.GetAllAsync(entityType, ct)));

        // Bulk-fetch items for all templates in one query (was one query per template).
        var itemsByTemplate = await _templateRepo.GetTemplateItemsByTemplatesAsync(
            templatesByType.SelectMany(t => t.Templates).Select(t => t.Id).ToList(), ct);

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

    private async Task<List<ExportRequestData>> BuildRequestDataAsync(
        List<SiteInfo> sites, Dictionary<Guid, string> criterionIdToKey, CancellationToken ct)
    {
        var resourceIdToName = new Dictionary<Guid, string>();
        var resourceIdToSiteCode = new Dictionary<Guid, string>();
        var allowedResourceIds = new HashSet<Guid>();

        // Bulk-fetch spaces for all sites in one query (was one query per site).
        var spacesBySite = await _resourceRepo.GetPlaceableBySitesAsync(sites.Select(s => s.Id).ToList(), ct);

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

        var allRequests = await _requestRepo.GetAllAsync(includeRequirements: true, ct: ct);

        var placeableKeySet = (await _resourceTypeRepo.GetPlaceableKeysAsync(ct)).ToHashSet();
        return allRequests
            .Select(r => (Request: r, SpaceResourceId: r.GetPlacementResourceId(placeableKeySet)))
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
