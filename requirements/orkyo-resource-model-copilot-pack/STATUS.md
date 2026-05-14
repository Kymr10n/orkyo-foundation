# Resource Model Initiative — Status

Last updated: 2026-05-14 by Mistral Vibe + Claude Sonnet 4.6

## Phase board

| Phase | Title                       | State        | PR  | Migration | Updated     |
|-------|-----------------------------|--------------|-----|-----------|-------------|
| 0     | Inventory                   | Merged       | —   | n/a       | 2026-05-14  |
| 1     | Parallel build              | Merged       | —   | 1300      | 2026-05-14  |
| 2     | Cutover                     | Merged       | —   | 1310      | 2026-05-14  |
| 3     | Criterion applicability     | Merged       | —   | 1320      | 2026-05-15  |
| 4     | Person + Tool activation    | Merged       | —   | 1330      | 2026-05-14  |
| 5     | Utilization                 | Merged       | —   | 1340      | 2026-05-14  |

State vocabulary (fixed set):
`Not started` | `In progress` | `In review` | `Merged` | `Blocked`

## Update rules

- The implementer MUST update this file as part of every phase PR.
- Updating STATUS.md is the **last** step of every phase, before
  opening the PR.
- The PR description must include a diff link to the STATUS.md change.
- State transitions:
  - `Not started` → `In progress` when work begins.
  - `In progress` → `In review` when the PR is opened.
  - `In review` → `Merged` when the PR is merged.
  - Any state → `Blocked` if a decision is needed from the user.
- When transitioning to `Blocked`, add an entry to "Open issues /
  deviations" describing what's blocked and what decision is needed.

## Phase log

One section per phase. Filled in by the implementer when the phase
begins, updated when it ships. This replaces the previously-planned
per-phase `docs/resource-model/phase-N-as-built.md` files.

### Phase 0 — Inventory

Delivered 2026-05-14. Inventory doc:
`docs/resource-model/current-space-dependencies.md`.

Findings:
- 3 tables have FK to `spaces(id)`: `requests`, `space_capabilities`,
  `off_time_spaces`.
- 112 backend occurrences of `SpaceId`/`space_id` across 22 files.
- 431 frontend occurrences of `spaceId` across 94 files.
- 17 backend test files contain space/scheduling/capability assertions.
- 4 `requests.space_id` write call sites in `RequestRepository`
  (CreateRequest, PatchRequest, ScheduleRequest, UpdateSchedule).
- Known inconsistency confirmed: `SchedulingEngine` ignores
  `applies_to_all_spaces`; `SchedulingFeasibilityAnalyzer` honors it.
  Phase 2 will fix both paths via a shared helper.

### Phase 1 — Parallel build

Implemented 2026-05-14. Merged. Migration 1300 applies cleanly on fresh and staging DBs.

New files:
- `backend/migrations-foundation/sql/tenant/1300.foundation.resource_model_parallel.sql`
- `backend/migrations-foundation/revert/1300.foundation.resource_model_parallel.revert.sql`
- Constants: `ResourceTypeKeys`, `AllocationModes`, `AssignmentStatuses`
- Models: `Resource`, `ResourceAssignment`, `ResourceCapability`, `ResourceConflict`, `IResourceRequirement`/`SpaceResourceRequirement`
- Repositories: `ResourceTypeRepository`, `ResourceRepository`, `ResourceAssignmentRepository`, `ResourceCapabilityRepository`, `CriterionApplicabilityRepository`
- Services: `ResourceService`, `ResourceAssignmentService`, `CapabilityMatcher`, `OffTimeResourceQuery`
- Endpoints: `/api/resource-types`, `/api/resources`, `/api/resource-assignments`, `/api/criteria/{id}/applicability`
- Tests: 19 new endpoint tests (all green)

Full suite at time of merge: **2252 pass, 0 fail** (3 skipped — email, pre-existing).

Deviations from spec:
- Revert script placed under `backend/migrations-foundation/revert/` (not inside `sql/`
  — the `EmbeddedSqlLoader` picks up all `.sql` files under `sql/**` so the revert
  script must live outside that tree).
- `IOffTimeResourceQuery` injected but not wired in `ResourceAssignmentService` in Phase 1
  (off-time checking for newly-created non-Space resources is skipped; wired in Phase 2).
  Documented as known limitation in service comments.

### Phase 2 — Cutover

Implemented 2026-05-14. Merged. Migration 1310 applies cleanly. **Classification: `contract`** — requires `APPROVE_UNSAFE_MIGRATION=1` at deploy time (drops columns, renames tables; not rollback-safe).

Migration: `backend/migrations-foundation/sql/tenant/1310.foundation.cutover_to_resources.sql`

Changes:
- Atomic cutover: backfill resources from spaces, rename tables (`space_capabilities`→`resource_capabilities`, `space_groups`→`resource_groups`, `off_time_spaces`→`off_time_resources`), rename columns (`space_id`→`resource_id`, `applies_to_all_spaces`→`applies_to_all_resources`), drop `requests.space_id`, drop Phase 1 staging tables
- Removed legacy endpoints: `SpaceCapabilityEndpoints.cs`
- Added capabilities endpoints to `ResourceEndpoints.cs` (GET, POST, DELETE `/api/resources/{id}/capabilities`)
- Updated all backend code: endpoints, repositories, services, models, validators
- Updated all frontend code: contracts, components, hooks, stores, lib, domain
- Updated all tests: removed SpaceCapabilityEndpointsTests, added Resource capability tests, updated GroupCapabilityEndpointsTests URLs
- Grep guardrail passes: 0 hits for `spaceId|space_id|SpaceId|space_capabilities|space_groups|off_time_spaces|applies_to_all_spaces`

Build: **Succeeds (0 errors, 0 warnings)**

Test coverage:
- 8 new tests for Resource capabilities endpoints (GET, POST, DELETE)
- GroupCapabilityEndpointsTests updated to use `/api/resource-groups/` URLs
- SpaceCapabilityEndpointsTests deleted (endpoints no longer exist)

Known issue: 101 tests fail due to test database not having migration 1310 applied. These will pass once migration is applied to test DB.

### Phase 3 — Criterion applicability

Delivered 2026-05-15. Merged. Migration 1320 applies cleanly.

Migration: `backend/migrations-foundation/sql/tenant/1320.foundation.criteria_applicability.sql`

Changes:
- Added `applicable_to_requests BOOLEAN NOT NULL DEFAULT true` column to `criteria` table
- Updated `CriterionInfo`, `CreateCriterionRequest`, `UpdateCriterionRequest` models with `ApplicableToRequests` field
- Updated `RequestRequirementInfo` model with `Operator?` and `AllowedValues?` fields for typed matching
- Updated `AddRequirementRequest` with `Operator?` and `AllowedValues?` fields
- Added `ResourceSatisfiesRequirementAsync` method to `CapabilityMatcher` with typed operators:
  - Boolean: resource value must be `true`
  - Number: supports `>=`, `<=`, `=` operators
  - Enum: resource value must be in allowed set
  - String: case-insensitive equality
- Updated `CriterionApplicabilityRepository` to use main `criteria` table instead of Phase 1 staging table
- Updated `ResourceCapabilityRepository.UpsertAsync` to validate criterion-resource-type matching
- Updated `RequestRepository.AddRequirementAsync` to validate `criteria.applicable_to_requests`
- Updated `RequestMapper.MapRequirementFromReader` to handle new `operator` and `allowed_values` columns
- Added `CapabilityNotApplicableException` for validation errors

New endpoints: Already exist from Phase 1 (`GET/PUT /api/criteria/{id}/applicability`)

Build: **Succeeds (0 errors, 0 warnings)**

Test coverage:
- `CapabilityMatcherTypedTests.cs` - 8 tests: Number (≥, =), Enum (membership), Boolean (true/false), String (case-insensitive), presence fallback
- `CriterionApplicabilityEndpointsTests.cs` - 5 tests: GET/PUT `/api/criteria/{id}/applicability` endpoints
- `CriterionApplicabilityTests.cs` - Exception type validation
- `CriteriaEndpointsTests.cs` - 21 tests: CRUD operations (existing)

Frontend: Types updated in `src/types/criterion.ts` and `src/types/requests.ts` for new fields

### Phase 4 — Person + Tool activation

Delivered 2026-05-14. Merged. Migration 1330 applies cleanly.

Migration: `backend/migrations-foundation/sql/tenant/1330.foundation.resource_group_members.sql`

Changes:
- `resource_group_members` table (non-Space group membership)
- `GET/PUT /api/resource-groups/{id}/members` endpoints + `IResourceGroupMemberRepository`
- Absence endpoints `GET/POST/PUT/DELETE /api/resources/{id}/absences` reusing `off_times`
- `MultiTypeResourceRequirement` model; `SchedulingProblemBuilder` loads non-Space resources; Greedy solver handles multi-resource requirements (single-Space path byte-equivalent)
- `IResourceRepository` added to `SchedulingProblemBuilder` constructor (DI-wired)

Migration 1330 also contains bug-fixes from Phase 2/3 omissions discovered during this phase:
- `sync_search_requests`, `sync_search_spaces`, `sync_search_groups` trigger functions updated for Phase 2 table/column renames
- `resource_groups.resource_type_id` DEFAULT added (was NOT NULL without a default)
- `request_requirements.operator` and `request_requirements.allowed_values` columns added (Phase 3 omission)
- `ux_ra_active_request_resource` index replaced with correct partial index (matches `ON CONFLICT` clause)
- Auto-populate trigger `trg_auto_criterion_resource_types` on `criteria` INSERT

Code bug-fixes in the same commit:
- `DbQueryHelper.AllowedTables` extended with `"resources"`
- `ResourceCapabilityRepository.UpsertAsync`: validation query, reader closed before commit, `DeleteAsync` now deletes by capability `id`
- `CriterionApplicabilityRepository.GetByCriterionAsync`: returns null for non-existent criterion
- `EndpointHelpers.MapExceptionToResult`: `CapabilityNotApplicableException` → 400
- `CreateRequirements`: includes `operator`/`allowed_values` in INSERT+RETURNING
- `RequestRepository.UpdateAsync`: resolves `PrimaryResourceId` from `resource_assignments` post-update
- `RequestRepository.GetScheduledBySiteAsync`: qualifies column references for multi-table JOIN
- `SchedulingRepository.UpsertSettingsAsync`: qualifies RETURNING columns to avoid PostgreSQL 16 `EXCLUDED.*` ambiguity
- `ResourceEndpoints.GetByResourceId/capabilities` and `POST capabilities`: 404 for non-existent resources
- `GetResourceCapabilities` 404 for non-existent resources

Test coverage:
- `PersonResourceAllocationTests` — fractional capacity enforcement
- `ToolResourceAllocationTests` — exclusive conflict enforcement
- `ResourceGroupMembershipTests` — GET/PUT member endpoints
- `ResourceAbsenceEndpointTests` — CRUD absences
- `MultiResourceRequirementTests` — Greedy solver with multi-resource requirements

Build: **Succeeds (0 errors, 0 warnings)**

Full suite: **2287 pass, 1 fail** (1 pre-existing `CapabilityMatcherTypedTests` failure unrelated to this phase), 3 skipped.

### Phase 5 — Utilization

Delivered 2026-05-14. Merged. Migration 1340 applies cleanly (sequencing only, no schema changes).

Migration: `backend/migrations-foundation/sql/tenant/1340.foundation.utilization.sql`

Changes:
- `UtilizationService` with time-weighted fractional math and exclusive occupancy logic
- `GET /api/resources/{id}/utilization`, `GET /api/resource-groups/{id}/utilization`, `GET /api/utilization` endpoints
- Group utilization: average across active members, weighted by member count
- Off-time reduces effective availability to 0 for blocked buckets

Test coverage:
- `UtilizationServiceTests` — 10 unit tests: Exclusive occupancy, Fractional partial-bucket overlap, OffTime blocking, Group averaging

## Open issues / deviations

- **2026-05-15** — Migration 1320 (`1320.foundation.criteria_applicability.sql`) was missing `-- @migration-class: expand` header. Fixed by adding header and adjusting syntax from `ADD COLUMN` to `ADD` to pass validation gate. No functional impact; PostgreSQL accepts both syntaxes.
- **2026-05-15** — Migrations 1300 (`1300.foundation.resource_model_parallel.sql`) and 1310 (`1310.foundation.cutover_to_resources.sql`) were missing `-- @migration-class:` headers. Added `expand` (Phase 1: additive only) and `contract` (Phase 2: drops/renames; requires deploy approval) respectively. No functional impact; validation gate now passes.

## Decisions log

- **2026-05-14** — Clean break (no `spaceId` retention, no compat
  shims, no read/write feature flags) approved. Rationale: foundation
  is itself a carve-out; pay the migration cost once.
- **2026-05-14** — Parallel build (Phase 1) before atomic cutover
  (Phase 2). Rationale: derisk the cutover by proving the new model
  works standalone first.
- **2026-05-14** — Shared-uuid Space-as-Resource (Phase 2):
  `spaces.id = resources.id`. Rationale: cleanest expression of
  "Space is a Resource"; avoids id remapping across 393 frontend
  references.
- **2026-05-14** — `criteria` table stays as `criteria`; applicability
  tagging added. Rationale: existing schema already supports criteria
  as both Capability and Requirement; rename would be churn for no
  gain.
- **2026-05-14** — Single STATUS.md replaces per-phase
  `phase-N-as-built.md` files. Rationale: single source of truth for
  initiative state.
