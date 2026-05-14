# Acceptance Criteria

Per-phase, observable. Each criterion can be checked by an
automated test or a deterministic script. Subjective criteria are
not acceptable.

## Phase 0

- File `orkyo-foundation/docs/resource-model/current-space-dependencies.md`
  exists and is non-empty.
- It lists every backend reference to `SpaceId`/`space_id` with file
  paths and line counts.
- It lists every frontend reference to `spaceId` with file paths and
  occurrence counts.
- It identifies the regression-baseline test files by exact name.

## Phase 1

- Migration `1300.foundation.resource_model_parallel.sql` applies
  cleanly on a fresh tenant database.
- All three `resource_types` seed rows exist with the correct keys
  and `is_system=true`.
- `dotnet test` passes including all new Phase 1 tests.
- The 130-test regression baseline passes unchanged.
- No edits to: `SchedulingEngine.cs`, `SchedulingService.cs`,
  `SchedulingProblemBuilder.cs`,
  `SchedulingFeasibilityAnalyzer.cs`, `SpaceService.cs`,
  `RequestService.cs`, `RequestRepository.cs`,
  `SpaceCapabilityRepository.cs`, any existing endpoint file.
- No frontend changes.
- All new endpoints reachable; non-admin returns 403.
- Revert script removes every new object cleanly on a fresh DB.
- `STATUS.md` Phase 1 row → `Merged`; Phase 1 log section updated.

## Phase 2

- Migration `1310.foundation.cutover_to_resources.sql` applies on a
  clone of staging data and its data-parity gate passes.
- `requests.space_id` column does not exist after migration.
- `space_capabilities`, `group_capabilities`, `space_groups`,
  `off_time_spaces` tables do not exist after migration.
- `applies_to_all_spaces` column does not exist after migration.
- `spaces` table has FK to `resources(id)`; `spaces.name` and
  `spaces.description` do not exist.
- Every row in `spaces` has a corresponding row in `resources` with
  the same id.
- Every pre-existing leaf request with a non-null `space_id` has a
  corresponding active row in `resource_assignments` (count parity).
- `grep -rn 'spaceId\|space_id\|SpaceId\|space_capabilities\|space_groups\|off_time_spaces\|applies_to_all_spaces' backend frontend` returns zero hits outside the `archive/` directory and historical migration SQL files.
- 130-test regression baseline passes with renamed identifiers and
  unchanged assertions.
- New regression test
  `SchedulingEngineHonorsAppliesToAllResourcesTests` passes
  (proving the previously-ignored flag is now honored).
- Frontend smoke: Space CRUD, request scheduling, off-time editor,
  auto-schedule preview all functional.
- Revert restores byte-equivalent pre-migration state on a fresh
  fixture (row-count + content checksums match).
- `STATUS.md` Phase 2 row → `Merged`; Phase 2 log section updated.

## Phase 3

- Migration `1320.foundation.criteria_applicability.sql` applies.
- `criteria.applicable_to_requests` column exists with default `true`.
- All existing criteria are in `criterion_resource_types` for type
  `space` (already true from Phase 1; verified by query).
- Capability assignment to a resource of an inapplicable type returns
  400 with body containing `CapabilityNotApplicable`.
- Requirement creation against a criterion with
  `applicable_to_requests=false` returns 400.
- `CapabilityMatcher` typed-operator tests pass (Number ≥/≤/=,
  Enum membership, Boolean, String).
- Endpoints `GET/PUT /api/criteria/{id}/applicability` reachable
  and admin-authorized.
- `STATUS.md` Phase 3 row → `Merged`; Phase 3 log section updated.

## Phase 4

- Admin can create a Person resource via `POST /api/resources` with
  `resourceTypeKey: "person"`. Default `allocation_mode` is
  `Fractional`.
- Admin can create a Tool resource. Default `allocation_mode` is
  `Exclusive`.
- Creating a Space via `POST /api/resources` is rejected (use the
  Space endpoint).
- A request can be created with a `resourceRequirements[]` array
  containing entries for both a Space and a Person; auto-schedule
  assigns one of each, with capability matching honored.
- Single-Space requests (no `resourceRequirements` field) behave
  byte-equivalent to today.
- Per-resource off-time endpoints work; an off-time blocks the
  resource in conflict detection.
- Frontend Resources admin area renders; CRUD + capability editor +
  absence editor functional.
- All existing tests pass.
- `STATUS.md` Phase 4 row → `Merged`; Phase 4 log section updated.

## Phase 5

- All three utilization endpoints reachable.
- Exclusive utilization tests pass.
- Fractional utilization tests pass.
- Group utilization tests pass.
- Off-time reduces effective availability to 0 in tests.
- No stored utilization field anywhere in the schema (verified by
  `\d+` inspection).
- `STATUS.md` Phase 5 row → `Merged`; Phase 5 log section updated.

## Done definition (whole initiative)

The initiative is done when:

- All Phase 1–5 acceptance criteria above are met.
- The 130-test regression baseline passes unchanged in assertion
  intent across every phase.
- `STATUS.md` shows every phase row as `Merged` with a populated
  Phase log section.
- No feature flags gating read/write paths exist anywhere in the
  codebase from this initiative.
- The CI guardrail `grep` for legacy identifiers returns zero hits.
