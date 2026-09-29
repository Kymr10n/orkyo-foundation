# Completed Initiatives

All items in `archive/` are fully shipped. This index is the entry point.

| Initiative | Completed | Files | What shipped |
|---|---|---|---|
| [Resource model](archive/domain-resource-model/2026-05-15-resource-model-complete/) | 2026-05-15 | 13 | Core domain refactor: Space/Resource/Person/Tool as first-class entities; OrgContext abstraction; multi-tenant-safe repositories |
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

## Superseded documents

[`archive/superseded-docs/`](archive/superseded-docs/) holds audits, plans and inventories that left `docs/`
and `frontend/docs/` in 2026-09. Each one describes a state of the code that no longer exists. They are historical records.

| Document | Written | Why it is here |
|---|---|---|
| `current-space-dependencies.md` | 2026-05 | Phase 0 inventory of the `spaces` side tables. Migrations 1700 and 1710 removed those tables. |
| `reusable-ui-candidates.md` | 2026-05 | Candidate list for shared UI components. Its own status note marks it as partially stale. |
| `main-branch-findings-2026-07.md` | 2026-07 | Review findings from 2026-07. Some files it names no longer exist. |
| `placement-audit-2026-05.md` | 2026-05 | Candidate list of product code that can move into foundation. It made no moves itself. |
| `data-model-schema-review-2026-06.md` | 2026-06 | Schema review. Migration 1570 still cites its §B2. |
| `generic-resource-types-review-2026-08.md` | 2026-08 | Review of the generic-resource-types branch. The branch is merged. |
| `calendar-feed-redesign-2026-08.md` | 2026-08 | Calendar feed redesign. It is implemented. Its open token-revocation item is closed by audit finding C1 (2026-09). |
| `resource-navigation-and-lists-spec.md` | 2026-08 | Navigation and lists spec. It is implemented, and it names files that no longer exist. |
| `lists-plan.md` | 2026-08 | Implementation plan for the spec above. Migration 1820 superseded part of it. |
| `tenant-transfer-plan.md` | 2026-08 | Four-repo plan for tenant export/import. Its status line says "not yet implemented". It is a work plan, not a reference for the code. |
| `design-review-2026-09.md`, `design-review-2026-09-plan.md`, `design-review-2026-09-progress.md` | 2026-09 | Design review, its remediation plan and the progress log. The plan and the log disagree about status. |
| `qr-resource-linking-plan.md` | 2026-09 | Implementation plan for QR stickers. It is implemented. The spec stays in `docs/` because code comments cite it. |
| `COVERAGE.md`, `UX-CONSISTENCY.md` | before 2026-09 | From `frontend/docs/`: coverage numbers and the UX friction audit. The coverage rule is now in `CLAUDE.md`. |
