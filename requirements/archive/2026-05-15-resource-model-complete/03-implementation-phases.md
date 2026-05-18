# Implementation Phases

Each phase = one PR, fully completing its slice. No half-states past
Phase 2. No feature flags gating read/write paths.

## Phase 0 — Inventory (docs only)

**Deliverable:** `orkyo-foundation/docs/resource-model/current-space-dependencies.md`.

Enumerate everything that will be touched in Phase 2:

- Every SQL FK pointing at `spaces(id)`.
- Every backend `SpaceId` / `space_id` reference (file + line counts).
- Every frontend `spaceId` reference (file + occurrence counts).
- Every test file that asserts on `spaceId` or Space-specific schema.
- The four current `space_id` write call sites in `RequestService`,
  `RequestRepository`, `SchedulingService`, `AutoSchedule`.

Lock down the regression baseline test list:

- `backend/tests/Services/AutoSchedule/*Tests.cs`
- `backend/tests/Services/SchedulingScenarioTests.cs`
- `backend/tests/Services/Scheduling*Tests.cs` (any others)
- `backend/tests/Endpoints/SchedulingEndpointTests*.cs`
- `backend/tests/Endpoints/AutoScheduleEndpointTests*.cs`

These behavioral assertions must remain green through every subsequent
phase. Only their identifier/property names are renamed in lockstep;
their *assertions* are never weakened.

No code change in Phase 0.

## Phase 1 — Parallel build (additive only)

Build the new resource model alongside the existing Space model.
No existing code path is modified. At end-of-phase, Space scheduling
works exactly as today, and the new model is independently provable
via unit + integration tests.

See `04-phase-1-parallel-build.md` for the detailed implementable spec.

## Phase 2 — Cutover (atomic)

Single PR that does the rename + drop + rewire. Frontend and backend
ship together. After this PR merges:

- `requests.space_id` is gone.
- `*_space*` tables and columns are renamed.
- `resource_assignments` is the canonical source of truth.
- `SchedulingEngine` / `SchedulingService` / `SchedulingProblemBuilder`
  read from `resource_assignments` and `off_time_resources`.
- `OR-Tools` and `Greedy` solver bodies are unchanged in behavior;
  they consume the new model via `IResourceRequirement`.
- Frontend uses `resourceId` everywhere; no `spaceId` references remain.

See `05-phase-2-cutover.md` for the detailed spec.

## Phase 3 — Criterion applicability tagging

Adds `criteria.applicable_to_requests` and `criterion_resource_types`.
Service-layer validation rejects mismatched assignments. New endpoint
`GET/PUT /api/criteria/{id}/applicability`. Capability matcher gains
typed operator support (Number with ≥/≤/=, Enum membership, Boolean
presence, String equality).

See `06-phase-3-applicability.md`.

## Phase 4 — Person + Tool activation

Admin CRUD for Person/Tool resources via `/api/resources`. Frontend
adds a Resources admin area; Space screens untouched. Request DTO
gains optional `resourceRequirements[]` with `(resourceTypeKey, count,
requiredCriterionIds)`; default behavior preserves single-Space
matching when the field is absent.

`MultiTypeResourceRequirement` joins `SpaceResourceRequirement` under
`IResourceRequirement`. Solvers extended only via the seam.

See `07-phase-4-person-tool.md`.

## Phase 5 — Utilization

Read-only utilization endpoints computed live from
`resource_assignments` + `off_time_resources` +
`resources.base_availability_percent`:

- `GET /api/resources/{id}/utilization?from=&to=`
- `GET /api/resource-groups/{id}/utilization?from=&to=`
- `GET /api/utilization?resourceTypeKey=&from=&to=`

No stored utilization values. See `08-phase-5-utilization.md`.

## Sequencing rules

- Phases ship in numbered order.
- Phase 2 may not begin until Phase 1's tests are green and merged.
- Phase 3 may begin in parallel with Phase 2 review **only if** it
  has no migration conflicts with Phase 2 (it adds new objects and
  one column; should not conflict). When in doubt, sequence strictly.
- Phase 4 and Phase 5 may be developed in parallel after Phase 3.
