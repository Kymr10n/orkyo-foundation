# Phase 6 — Read-Model Unification

## Goal

Eliminate the leftover `primary_resource_id` read-model artifacts from
the post-cutover codebase. Replace them with an honest, multi-resource-
aware view: `RequestInfo.Assignments` exposes **all** non-cancelled
resource assignments (Space, Person, Tool), backed by a single SQL view
that all read paths share. Unify DTO field naming. Drop manual
in-memory model patching in repository writes by re-reading after
commit.

Single PR, BE + FE in lockstep. No backward-compat shims, no synonyms,
no read/write flags.

## Why

The 5-phase Resource Model Initiative made `resource_assignments` the
single source of truth and dropped `requests.space_id` in migration
1310. But several read-model leftovers persisted:

1. **`PrimaryResourceSubquery`** in
   `backend/src/Repositories/RequestRepository.cs:23-30` — a SQL
   fragment hardcoded to `rt.key = 'space'`, copy-pasted into ~6 SELECT
   queries, and a parallel `NullResourceId` constant used in INSERT/
   UPDATE RETURNING clauses.

2. **`RequestInfo.PrimaryResourceId`** exposes only the space
   assignment. Person/Tool assignments created by the Phase 4 Greedy
   multi-resource solver are invisible in API responses.

3. **Inconsistent DTO naming**: `Create/UpdateRequestRequest.ResourceId`
   vs. `ScheduleRequestRequest.PrimaryResourceId` (the latter renamed
   in a 2026-05-14 hotfix to match the FE JSON contract).

4. **Manual `with { PrimaryResourceId = ... }` patches** in repository
   writes — relies on developer discipline to keep the in-memory model
   synced with what was just written to `resource_assignments`.

5. **The "primary" abstraction lies.** It is not "the primary
   resource"; it is "the first non-cancelled space assignment". The
   name hides this from every caller.

orkyo-saas and orkyo-community frontends have zero references to
`primaryResourceId`, so the blast radius is contained to
orkyo-foundation.

## DB

**Migration 1350 — `1350.foundation.request_assignments_view.sql`**
Classification: `expand` (additive only; one `CREATE VIEW`).

```sql
-- @migration-class: expand
CREATE VIEW v_requests_with_assignments AS
SELECT
    r.*,
    COALESCE(
      (SELECT jsonb_agg(jsonb_build_object(
          'id',                 ra.id,
          'resource_id',        ra.resource_id,
          'resource_type_key',  rt.key,
          'start_utc',          ra.start_utc,
          'end_utc',            ra.end_utc,
          'allocation_percent', ra.allocation_percent,
          'allocation_units',   ra.allocation_units,
          'assignment_status',  ra.assignment_status,
          'created_at',         ra.created_at,
          'updated_at',         ra.updated_at
        ) ORDER BY rt.key, ra.start_utc)
       FROM resource_assignments ra
       JOIN resources res     ON res.id = ra.resource_id
       JOIN resource_types rt ON rt.id  = res.resource_type_id
       WHERE ra.request_id = r.id
         AND ra.assignment_status != 'Cancelled'),
      '[]'::jsonb
    ) AS assignments
FROM requests r;
```

Revert: `backend/migrations-foundation/revert/1350.foundation.request_assignments_view.revert.sql`
contains `DROP VIEW IF EXISTS v_requests_with_assignments;`.

The view is read-only (no `INSTEAD OF` triggers). Writes target the
base tables. Ordering by `(rt.key, ra.start_utc)` is a stable
contract that snapshot tests may rely on.

## Backend model

**`RequestInfo` (`Models/Request.cs`)**
- Remove `Guid? PrimaryResourceId` (line 82).
- Add `public required IReadOnlyList<ResourceAssignmentInfo> Assignments { get; init; }` — never null; default `[]`.
- Redefine `IsScheduled` (line 118):
  ```csharp
  public bool IsScheduled =>
      StartTs.HasValue && EndTs.HasValue
      && Assignments.Any(a => a.ResourceTypeKey == ResourceTypeKeys.Space);
  ```

**`ResourceAssignmentInfo` (`Models/ResourceAssignment.cs`)**
- Add `public required string ResourceTypeKey { get; init; }` (view
  supplies `rt.key`; no extra lookup at the C# layer).

**DTO field-name unification** — canonical name: `resourceId`.
- `ScheduleRequestRequest.PrimaryResourceId` → `ResourceId`
  (Models/Request.cs:264). This reverses the 2026-05-14 hotfix; the FE
  flips in lockstep so the JSON contract still matches.
- `CreateRequestRequest.ResourceId` and `UpdateRequestRequest.ResourceId`
  are unchanged.

**Extension method** (in `Models/Request.cs` or
`Models/RequestInfoExtensions.cs`) — single canonical helper for "get
the space-assignment resource id":

```csharp
public static Guid? GetSpaceResourceId(this RequestInfo r) =>
    r.Assignments.FirstOrDefault(a => a.ResourceTypeKey == ResourceTypeKeys.Space)?.ResourceId;
```

## Repository refactor

**`RequestRepository.cs`**
- Delete `PrimaryResourceSubquery` (23-30) and `NullResourceId` (33).
- Introduce a `SelectFromView` constant selecting from
  `v_requests_with_assignments` by explicit column name (including
  `assignments`).
- Refactor SELECT call sites to read from the view: `GetAllAsync` (51),
  `GetAllAsync(PageRequest)` (75), `GetByIdAsync` (127),
  `GetChildrenAsync` (684).
- Rewrite `GetScheduledBySiteAsync` (94-109) to filter via
  `EXISTS (SELECT 1 FROM resource_assignments ra JOIN spaces s ...)`.
  No DISTINCT, no row explosion.
- **Writes re-read after commit.** Change `RETURNING ...` to
  `RETURNING id`, then `await ReadByIdAsync(db, id)` (new private
  helper) after `tx.CommitAsync()`. Delete the manual
  `with { PrimaryResourceId = ... }` patches at lines 218, 226, 330,
  354-362, 439. Affected: `CreateAsync`, `UpdateAsync`,
  `UpdateScheduleAsync`, `MoveAsync`. One extra round-trip per write
  — acceptable; `UpdateAsync` was already doing two for post-hoc PID
  resolution.
- `BatchUpdateSchedulesAsync` (446-497): no model patching to remove.
  Consume `request.ResourceId` per the DTO rename.

**`RequestMapper.cs`**
- Drop `PrimaryResourceId = reader.GetNullableGuid("primary_resource_id")`
  (line 20).
- Add `Assignments = ParseAssignments(reader.GetString("assignments"))`
  via private `System.Text.Json` helper. `JsonPropertyName` attributes
  map snake_case → record fields. Empty JSON `[]` returns `[]`.

## Adapter sites

Replace all `.PrimaryResourceId` reads with the extension or DTO
rename:

- `Services/AutoSchedule/SchedulingProblemBuilder.cs:78` —
  `r.PrimaryResourceId!.Value` → `r.GetSpaceResourceId()!.Value`
  (already gated by new `IsScheduled`).
- `Services/ExportService.cs:270-277` — three call sites use
  `GetSpaceResourceId()`.
- `Services/SchedulingService.cs:86,135,156,167` — DTO rename + use
  extension on `existing`.
- `Services/AutoSchedule/AutoScheduleService.cs:99` —
  `PrimaryResourceId = a.ResourceId` → `ResourceId = a.ResourceId`.
- `Endpoints/RequestEndpoints.cs:109` and
  `Validators/ScheduleRequestRequestValidator.cs:12` — DTO rename.

## Frontend

**Types (`types/requests.ts`)**
- New types: `ResourceAssignment`, `AssignmentStatus`, `ResourceTypeKey`.
- `Request`: drop `primaryResourceId?`; add
  `assignments: ResourceAssignment[]` (never optional — server always
  returns `[]`).
- `CreateRequestRequest.primaryResourceId` → `resourceId` (line 112).
- `UpdateRequestRequest.primaryResourceId` → `resourceId` (line 139).

**API client (`lib/api/utilization-api.ts`)**
- `ScheduleRequestData.primaryResourceId` → `resourceId` (line 23).

**New helper module (`domain/scheduling/request-assignments.ts`)**
```ts
export function getSpaceAssignment(r: Request): ResourceAssignment | null;
export function getSpaceResourceId(r: Request): string | null;
export function getAssignmentsByType(r: Request, key: ResourceTypeKey): ResourceAssignment[];
export function applySpaceAssignmentOptimistic(
  r: Request, resourceId: string, startUtc: string, endUtc: string
): Request;
```
Plus matching `*.test.ts`.

**New constants (`lib/constants/resource-types.ts`)** — mirror BE
`ResourceTypeKeys`:
```ts
export const RESOURCE_TYPE_KEYS = {
  SPACE: 'space', PERSON: 'person', TOOL: 'tool'
} as const;
```

**File-by-file (42 references; grouped):**

| Category | Files | Change |
|---|---|---|
| Space-row filter | `components/utilization/SpaceRow.tsx:47`, `CollapsibleFloorplan.tsx`, `ScheduledRequestOverlay.tsx`, `RequestsPanel.tsx`, `pages/UtilizationPage.tsx` | `r.primaryResourceId === space.id` → `getSpaceResourceId(r) === space.id` |
| Schedule conversion | `domain/scheduling/schedule-model.ts:94-100` | Use `getSpaceAssignment(request)` |
| Mutation hooks | `hooks/useUtilization.ts:55,76`, `hooks/useSchedulingConflicts.ts:59-79`, `hooks/useRequestForm.ts:163` | Optimistic update via `applySpaceAssignmentOptimistic`; payload uses `resourceId` |
| Form payloads | `lib/utils/utils.ts:145,167` | `buildCreatePayload`/`buildUpdatePayload` write `resourceId` |
| Page handlers | `pages/UtilizationPage.tsx:274,319,359,364` | Pass `resourceId` to mutation |
| Details dialog | `components/requests/RequestDetailsDialog.tsx:218,226` | Show `getSpaceAssignment(r)?.resourceId`; additionally render full assignment list (Person/Tool become user-visible) |
| Export / Gantt | `lib/utils/gantt-pdf-export.ts:54,85`, `lib/utils/export-handlers.ts:122` | `getSpaceResourceId(r)` |

## Tests

**Backend**
- `tests/Validators/ScheduleRequestValidatorTests.cs` — already
  references `ResourceId`; green again after the DTO rename.
- `tests/Models/RequestModelsTests.cs:238` — replace
  `PrimaryResourceId = resourceId` fixture with an `Assignments`
  collection. Rename `IsScheduled_FalseWhenResourceIdNull` →
  `IsScheduled_FalseWhenNoSpaceAssignment`.
- `tests/Endpoints/RequestEndpointsTests.cs` — ~10 sites (51, 416,
  1116, 1131, 1157, 1172, 1350, 1390): replace `created.PrimaryResourceId`
  with `created.Assignments.SingleOrDefault(a => a.ResourceTypeKey == "space")?.ResourceId`.
  Add a `SpaceResourceId()` test-helper extension under
  `tests/TestHelpers/`.

**New backend tests**
- `RequestRepository_GetById_ReturnsAllAssignmentTypes` — Space +
  Person, both surface.
- `RequestRepository_GetById_ExcludesCancelledAssignments`.
- `View_v_requests_with_assignments_ReturnsEmptyArrayForUnassignedRequests`.
- `MultiResourceScheduling_ExposesBothAssignmentsInRequestInfo` —
  regression: Greedy solver schedules `[Space, Person]`, `GET
  /api/requests/{id}` returns both.

**Frontend**
- New fixture factory `src/test-utils/request-fixtures.ts` exposes
  `makeRequest({ spaceId?, ... })` to keep ~25 test files terse.
- Update ~25 `.test.{ts,tsx}` files via the factory.
- New `request-assignments.test.ts` covers the helper module.
- Update `schedule-model.test.ts` to cover mixed-assignment requests.

## Sequencing within the PR

Three commits for review ergonomics. Working tree compiles only at
each commit boundary (acceptable — clean break):

1. **`feat(be): add v_requests_with_assignments view and assignments
   read model`** — migration 1350 + revert, model/DTO changes, repo
   refactor, mapper, all backend adapter sites, backend tests.
   `dotnet build` clean and `dotnet test` green at this commit.
2. **`feat(fe): switch Request model to assignments list`** — types,
   helper module, constants, fixture factory, all 42 file conversions,
   frontend tests. `pnpm tsc --noEmit` clean and vitest green.
3. **`docs: STATUS.md — Phase 6 read-model unification`** — phase
   board row, phase log entry, decisions log entry.

Commits 1 and 2 must land together (BE returns `assignments`, FE
expects `assignments`).

## Acceptance / verification

1. **DB**: apply 1350 on a fresh DB (full `1000…1350` chain) and on a
   staging copy. Assert `SELECT count(*) FROM v_requests_with_assignments
   = SELECT count(*) FROM requests`. Spot-check 5 requests where
   `assignments[0]->>'resource_id'` matches the prior
   `PrimaryResourceSubquery` result.
2. **Backend**: `dotnet build` clean. `dotnet test` from `backend/`
   green. Expect ≥2287 pass (STATUS baseline), 0 fail. New tests ≈5.
3. **Frontend**: `pnpm tsc --noEmit` clean; `pnpm vitest run` green;
   `pnpm lint` clean.
4. **Manual smoke** on a dev tenant (Community on port 5002):
   - `GET /api/requests/{id}` for an unscheduled request →
     `assignments: []`.
   - `PATCH /api/requests/{id}/schedule` with
     `{ resourceId, startTs, endTs }` → request returned with one
     space assignment.
   - Re-`GET`: identical payload (write/read parity).
   - Utilization page: drag-drop a request → renders in correct
     `SpaceRow`; optimistic state visually identical to today.
   - Auto-schedule apply with a Person requirement → response request
     has both space and person in `assignments[]`.
   - Export to JSON via existing export flow → `ResourceName` /
     `SiteCode` populated via `GetSpaceResourceId`.
5. **`EXPLAIN ANALYZE`** the list endpoint on staging-sized data
   before merge — confirm correlated `jsonb_agg` performs comparably
   to today's `LIMIT 1` correlated subquery. Fallback: rewrite the
   view as `LEFT JOIN LATERAL` with `GROUP BY r.id`.

## Risks & open questions

1. **`jsonb_agg` performance** on the list endpoint vs. today's
   `LIMIT 1` correlated subquery. Same correlated-subquery shape;
   benchmark on staging before merge.
2. **View is non-materialized.** Cannot be indexed; relies on the
   existing partial index `(request_id, resource_id) WHERE
   assignment_status != 'Cancelled'`. Materialization is out of scope
   (would require a refresh strategy and add staleness).
3. **Per-tenant view creation.** New tenants onboarded mid-deploy
   receive 1350 via the standard tenant-migration pipeline
   (`FoundationMigrationModule`). No special-casing needed.
4. **Cross-repo API-contract verification.** Confirmed: orkyo-saas
   and orkyo-community have zero `primaryResourceId` references at
   plan time. Re-run `grep -r primaryResourceId` in both at
   PR-open time as a final guard.
5. **`Assignments` ordering contract.** View orders by
   `(rt.key, ra.start_utc)`. Document this in `RequestInfo.Assignments`
   XML doc-comment so FE snapshot tests can rely on it.
6. **Existing FE snapshot tests** capturing a JSON `Request` payload
   will diff (no `primaryResourceId` key, new `assignments` array).
   Refresh snapshots as part of the FE commit.

## Critical files

**Backend**
- `backend/migrations-foundation/sql/tenant/1350.foundation.request_assignments_view.sql` (new)
- `backend/migrations-foundation/revert/1350.foundation.request_assignments_view.revert.sql` (new)
- `backend/src/Models/Request.cs`
- `backend/src/Models/ResourceAssignment.cs`
- `backend/src/Repositories/RequestRepository.cs`
- `backend/src/Repositories/RequestMapper.cs`
- `backend/src/Validators/ScheduleRequestRequestValidator.cs`
- `backend/src/Endpoints/RequestEndpoints.cs`
- `backend/src/Services/SchedulingService.cs`
- `backend/src/Services/ExportService.cs`
- `backend/src/Services/AutoSchedule/AutoScheduleService.cs`
- `backend/src/Services/AutoSchedule/SchedulingProblemBuilder.cs`
- `backend/tests/**` (Models, Endpoints, Validators)

**Frontend**
- `frontend/src/types/requests.ts`
- `frontend/src/lib/api/utilization-api.ts`
- `frontend/src/domain/scheduling/request-assignments.ts` (new)
- `frontend/src/lib/constants/resource-types.ts` (new)
- `frontend/src/test-utils/request-fixtures.ts` (new)
- 42 source/test files currently using `primaryResourceId`

**Docs**
- `STATUS.md` (root) — new phase row + phase log entry

## Reusable existing utilities

- `ResourceTypeKeys.Space` in `backend/src/Constants/ResourceTypeKeys.cs`
  — already exists; use everywhere for the type-key comparison.
- `AssignmentStatuses` constants — already used by
  `WriteResourceAssignmentAsync`/`CancelSpaceAssignmentAsync`. No
  change needed.
- `EmbeddedSqlLoader` + `FoundationMigrationModule` — picks up
  `*.sql` under `sql/**`; revert script under `revert/` is correctly
  excluded (per Phase 1 deviation note in STATUS.md).
- `WebApplicationFactory` integration test setup — new view/repo
  tests slot in without new infrastructure.
