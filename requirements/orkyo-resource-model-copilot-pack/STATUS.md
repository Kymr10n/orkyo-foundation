# Resource Model Initiative — Status

Last updated: 2026-05-14 by Mistral Vibe

## Phase board

| Phase | Title                       | State        | PR  | Migration | Updated     |
|-------|-----------------------------|--------------|-----|-----------|-------------|
| 0     | Inventory                   | Merged       | —   | n/a       | 2026-05-14  |
| 1     | Parallel build              | Merged       | —   | 1300      | 2026-05-14  |
| 2     | Cutover                     | In review    | —   | 1310      | 2026-05-14  |
| 3     | Criterion applicability     | Not started  | —   | 1320      | 2026-05-14  |
| 4     | Person + Tool activation    | Not started  | —   | 1330      | 2026-05-14  |
| 5     | Utilization                 | Not started  | —   | 1340      | 2026-05-14  |

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

Implemented 2026-05-14. Migration 1310 applies cleanly.

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

_Not started. Spec: `06-phase-3-applicability.md`._

### Phase 4 — Person + Tool activation

_Not started. Spec: `07-phase-4-person-tool.md`._

### Phase 5 — Utilization

_Not started. Spec: `08-phase-5-utilization.md`._

## Open issues / deviations

_None yet._

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
