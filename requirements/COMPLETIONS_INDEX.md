# Completed Initiatives

All items in `archive/` are fully shipped. This index is the entry point.

| Initiative | Completed | Files | What shipped |
|---|---|---|---|
| [Resource model](archive/domain-resource-model/2026-05-15-resource-model-complete/) | 2026-05-15 | 23 | Core domain refactor: Space/Resource/Person/Tool as first-class entities; OrgContext abstraction; multi-tenant-safe repositories |
| [Criterion applicability](archive/domain-criteria/2026-05-15-criterion-applicability-complete/) | 2026-05-15 | 6 | Scope tags on criteria (Space / People / Tool); consistent filtering across Settings and resource-domain pages |
| [People resources](archive/domain-people/2026-05-16-people-resources-complete/) | 2026-05-16 | 4 | People as bookable resources; capacity, skills, availability surfaced through the resource model |
| [Availability model](archive/domain-availability/2026-05-28-availability-model-complete/) | 2026-05-28 | 1 | `availability_events` + `resource_absences` replacing `off_times`; migration 1490 |
| [Reporting integration API](archive/domain-reporting/2026-05-28-reporting-api-complete/) | 2026-05-28 | 6 | Tenant-safe Reporting Integration API; 7 phases, 16 tests; replaces embedded reporting engine |
| [Criteria & skills](archive/domain-criteria/2026-05-31-criteria-skills-complete/) | 2026-05-31 | 5 | Skills as a dimension of criteria; skill-level matching in scheduling engine |
| [DB asset storage](archive/domain-assets/2026-05-31-db-assets-complete/) | 2026-05-31 | 6 | Floorplans and managed assets moved from filesystem into Postgres-backed asset storage |
| [People reference data](archive/domain-people/2026-05-31-people-reference-data-complete/) | 2026-05-31 | 2 | Departments and job titles as managed reference data |
| [Resource domain UI](archive/domain-resource-ui/2026-05-31-resource-domain-ui-complete/) | 2026-05-31 | 8 | Domain administration UX refactor; unified resource management pages |
| [Resource group typing](archive/domain-resource-model/2026-05-31-resource-group-typing-complete/) | 2026-05-31 | 2 | Typed resource groups (capacity groups, skill groups); group-aware scheduling |
| [People utilization segments](archive/domain-utilization/2026-06-07-people-utilization-segments-complete/) | 2026-06-07 | 3 | People utilization grid refactored from background heatmap to read-only aggregated timeline segments; period-scoped assignment dialog |
| [Home-site resource model](archive/domain-resource-model/2026-06-14-home-site-resource-model-complete/) | 2026-06-14 | 5 | Unified resource location model: stored `home_site_id` + `cross_site_allowed`, derived read-only `currentSiteId` (drop migration 1560), cross-site request validation |
| [Request calendar view](archive/domain-utilization/2026-06-22-calendar-view-for-requests-complete/) | 2026-06-22 | 1 | FullCalendar-based day/week/month calendar for requests; drag/resize reschedule and empty-slot scheduling |
| [Built-in insights](archive/domain-insights/2026-06-23-insights-complete/) | 2026-06-23 | 8 | Tenant-safe built-in insights semantic layer; overview/utilization/conflicts/requests APIs + KPI & trend dashboard, composed into saas + community |
