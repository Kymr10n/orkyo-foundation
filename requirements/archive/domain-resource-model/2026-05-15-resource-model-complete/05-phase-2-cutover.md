# Phase 2 — Cutover

**Implementable spec.** This document is the contract for the Phase 2
PR. Only begin after Phase 1 has merged and its tests are green on
main.

## Goal

One PR that atomically:

1. Backfills `resources` rows for every existing space (shared uuid).
2. Makes `spaces` a subtype-extension table of `resources`.
3. Renames `*_space*` tables and columns to `*_resource*`.
4. Drops `requests.space_id`, backfilling `resource_assignments` from
   the column's data first.
5. Rewires `SchedulingEngine`, `SchedulingService`,
   `SchedulingProblemBuilder`, `SchedulingFeasibilityAnalyzer`,
   `SpaceService`, `RequestService`, `RequestRepository` to use the
   new model.
6. Drops the Phase-1 staging tables (`resource_capabilities_phase1`,
   `criteria_applicability_phase1`).
7. Renames backend DTO fields and frontend code in lockstep
   (`spaceId` → `resourceId`, `SpaceIds` → `ResourceIds`, etc.).

After this PR merges:
- `grep -r 'space_id\|spaceId\|SpaceId\|space_capabilities\|space_groups\|off_time_spaces\|applies_to_all_spaces' backend frontend` returns zero hits outside of historical migration SQL.
- The 130-test regression baseline from Phase 0 passes with renamed
  identifiers but unchanged assertions.

## Migration

Files:
- `backend/migrations-foundation/sql/tenant/1310.foundation.cutover_to_resources.sql`
- `backend/migrations-foundation/sql/tenant/revert/1310.foundation.cutover_to_resources.revert.sql`

The migration body must execute in a single transaction. Order:

1. **Backfill resources from spaces.**
   ```sql
   INSERT INTO resources (id, resource_type_id, name, description,
                          allocation_mode, base_availability_percent,
                          is_active, created_at, updated_at)
   SELECT s.id,
          (SELECT id FROM resource_types WHERE key='space'),
          s.name, s.description,
          'Exclusive', 100, true,
          s.created_at, s.updated_at
   FROM   spaces s
   ON CONFLICT (id) DO NOTHING;
   ```

2. **Migrate `space_capabilities` data to the Phase-1 staging table**
   (so the legacy table can be renamed without conflict), or — simpler
   — drop the Phase-1 staging table empty:
   ```sql
   DROP TABLE resource_capabilities_phase1;
   DROP TABLE criteria_applicability_phase1;
   ```

3. **Renames** (exact SQL in `02-database-schema.md`):
   - `space_capabilities` → `resource_capabilities` (+ column rename
     + FK retarget).
   - `space_groups` → `resource_groups` (+ `resource_type_id` column).
   - `group_capabilities` → `resource_group_capabilities` (+ column
     rename).
   - `off_time_spaces` → `off_time_resources` (+ column rename + FK
     retarget).
   - `off_times.applies_to_all_spaces` → `applies_to_all_resources`.

4. **Subtype `spaces` under `resources`:**
   ```sql
   ALTER TABLE spaces
     ADD CONSTRAINT spaces_id_resource_fkey
     FOREIGN KEY (id) REFERENCES resources(id) ON DELETE RESTRICT;
   ALTER TABLE spaces DROP COLUMN name;
   ALTER TABLE spaces DROP COLUMN description;
   ```

5. **Backfill `resource_assignments` from `requests.space_id`:**
   ```sql
   INSERT INTO resource_assignments
     (id, request_id, resource_id, start_utc, end_utc,
      assignment_status, created_at, updated_at)
   SELECT gen_random_uuid(), r.id, r.space_id,
          r.start_ts, r.end_ts,
          CASE r.status WHEN 'cancelled' THEN 'Cancelled' ELSE 'Planned' END,
          r.created_at, r.updated_at
   FROM   requests r
   WHERE  r.space_id IS NOT NULL
     AND  r.start_ts IS NOT NULL
     AND  r.end_ts   IS NOT NULL
     AND  r.planning_mode = 'leaf';
   ```

6. **Drop `requests.space_id`:**
   ```sql
   ALTER TABLE requests DROP CONSTRAINT requests_space_id_fkey;
   DROP INDEX  IF EXISTS idx_requests_space_id;
   DROP INDEX  IF EXISTS idx_requests_scheduling_join;
   ALTER TABLE requests DROP COLUMN space_id;
   ```

7. **Data-parity gate** at the end of the migration:
   ```sql
   DO $$
   DECLARE
     expected_count BIGINT;
     actual_count   BIGINT;
   BEGIN
     SELECT COUNT(*) INTO expected_count FROM requests_backup_phase2
     WHERE space_id IS NOT NULL AND start_ts IS NOT NULL
       AND end_ts IS NOT NULL AND planning_mode = 'leaf';
     SELECT COUNT(*) INTO actual_count FROM resource_assignments;
     IF expected_count <> actual_count THEN
       RAISE EXCEPTION 'Data parity gate failed: expected % resource_assignments, got %',
                       expected_count, actual_count;
     END IF;
   END $$;
   ```
   (The migration creates `requests_backup_phase2` as a CREATE TABLE
   AS SELECT before step 5 and drops it at the end after the gate
   passes.)

## Code rewrites

### Backend

- **DTOs / contracts:**
  - `RequestInfo.SpaceId` → removed; add `ResourceAssignments: List<ResourceAssignmentInfo>`.
  - `ProposedAssignmentDto.SpaceId` → `ResourceId`.
  - `OffTimeInfo.SpaceIds` → `ResourceIds`.
  - `OffTimeInfo.AppliesToAllSpaces` → `AppliesToAllResources`.
  - `CreateOffTimeRequest.SpaceIds` → `ResourceIds`.
  - `UpdateOffTimeRequest.SpaceIds` → `ResourceIds`.

- **Services:**
  - `SchedulingService` and `SchedulingEngine` query
    `resource_assignments` (joined to `resources` of type=space when
    legacy Space conflict detection is needed) and `off_time_resources`.
  - Off-time `applies_to_all_resources=true` is now honored by **both**
    `SchedulingEngine` and `SchedulingFeasibilityAnalyzer` via a
    single helper `OffTimeFilter.AppliesTo(resourceId, siteId)`.
  - `SpaceService.CreateAsync` creates the `resources` row first, then
    the `spaces` row with the same id (single transaction).
  - `SpaceService.UpdateAsync(name|description)` writes to `resources`.
  - `SpaceService.DeleteAsync` deletes the `spaces` row and, if no
    assignments reference it, the `resources` row.
  - `RequestService` and `RequestRepository` no longer touch
    `space_id`. The four write call sites identified in Phase 0
    write/cancel `resource_assignments` instead.

- **AutoSchedule:**
  - `SchedulingProblemBuilder` switches to `IResourceRequirement`,
    yielding one `SpaceResourceRequirement` per request (preserving
    today's single-Space behavior).
  - `SchedulingFeasibilityAnalyzer.Analyze()` operates on resources
    (not spaces). Its capability-matching logic is unchanged in
    spirit: `request.RequiredCriterionIds ⊆ resource.CriterionIds`.
  - OR-Tools / Greedy solver bodies are unchanged in behavior.
    They consume the resource ids from the problem builder; nothing
    about variable layout, search strategy, or objective function
    changes.

- **Endpoints (renames):**
  - `/api/sites/{siteId}/spaces/{spaceId}/capabilities` →
    `/api/resources/{resourceId}/capabilities`. The old endpoint is
    removed; the file `SpaceCapabilityEndpoints.cs` is renamed to
    `ResourceCapabilityEndpoints.cs` and its content rewritten.
  - `/api/groups/{groupId}/capabilities` →
    `/api/resource-groups/{groupId}/capabilities`. File rename and
    rewrite.
  - `SpaceEndpoints.cs` — unchanged paths (`/api/sites/{siteId}/spaces`),
    but write methods now coordinate `resources` + `spaces` via
    `SpaceService`.
  - `RequestEndpoints.cs` — request response shape gains
    `resourceAssignments`. The old `spaceId` field is removed.
  - `AutoScheduleEndpoints.cs` — preview returns `resourceId` not
    `spaceId`.

- **Repositories:**
  - `SpaceCapabilityRepository.cs` → `ResourceCapabilityRepository.cs`.
  - `SchedulingRepository.cs` — every SQL query updated:
    `off_time_spaces` → `off_time_resources`, `space_id` →
    `resource_id`, `applies_to_all_spaces` → `applies_to_all_resources`.
  - `RequestRepository.cs` — drop all `space_id` references; loads
    `resource_assignments` for each request and maps them into the
    DTO.

### Frontend

Single coordinated PR (same PR as backend).

- `frontend/contracts/autoSchedule.ts` — `spaceId` → `resourceId`.
- All `frontend/src/**/*.{ts,tsx}` references to `spaceId` →
  `resourceId`. Hook names `useSpaceCapabilities` →
  `useResourceCapabilities`. React Query keys updated.
- Off-time editor: `applies_to_all_spaces` → `applies_to_all_resources`;
  `spaceIds` → `resourceIds`.
- User-facing labels keep the word "Space" because Space remains a UX
  concept. Only IDs and field names change.
- A post-merge CI check: `grep -r 'spaceId\|space_id\|SpaceId' backend frontend | grep -v archive | grep -v 'historical migration'` must return zero. Add as a CI job.

## Tests

- Every existing scheduling/capability/auto-schedule test renamed in
  lockstep. Behavioral assertions preserved.
- New `SchedulingEngineHonorsAppliesToAllResourcesTests` —
  regression test for the bug that's being fixed in this phase
  (engine previously ignored `applies_to_all_spaces`).
- Data-parity migration test: load a fixture with N requests of
  varying status/leaf-mode/space-id values; run migration; assert
  exactly the expected `resource_assignments` rows exist.

## Definition of done

- Phase 2 migration applies cleanly on a staging clone with the
  data-parity gate green.
- Phase 2 revert restores the pre-migration state byte-equivalent
  (verified by row-count + checksum comparison on a fresh fixture).
- Every regression-baseline test passes with renamed identifiers.
- `grep` check for `spaceId | space_id | SpaceId | space_capabilities
  | space_groups | off_time_spaces | applies_to_all_spaces` returns
  zero hits in `backend` and `frontend` outside the `archive/` folder
  and historical migration SQL.
- `STATUS.md` updated: Phase 2 row → `Merged`; Phase 2 log section
  describes what shipped and any deviations.

## Risk register

- **Single PR, large blast radius.** Mitigation: the migration is
  guarded by the data-parity gate. Every file change is mechanical
  rename plus the four localized write-path rewrites. The regression
  test baseline catches behavioral drift.
- **Frontend/backend lockstep.** Both must ship together. Mitigation:
  single PR; the CI grep guardrail prevents accidental retention.
- **Solver behavioral drift.** Mitigation: the
  `OrToolsSchedulingSolverTests` and `GreedySchedulingSolverTests`
  assertions are unchanged in this phase; they merely consume renamed
  identifiers. Any drift fails the test run.
