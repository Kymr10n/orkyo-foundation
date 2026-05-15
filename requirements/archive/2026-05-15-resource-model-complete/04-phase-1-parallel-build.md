# Phase 1 — Parallel Build (Additive Only)

**Implementable spec.** This document is the contract for an autonomous
implementer (Claude Sonnet) to produce the Phase 1 PR.

## Goal

Introduce the new resource model alongside the existing Space model
**without modifying any production code path**. At end-of-phase:

- New tables, repositories, services, and endpoints exist.
- The new model is provably correct via unit + integration tests.
- Existing Space scheduling behaves exactly as before.
- No frontend changes required (new endpoints are admin-only and
  consumed only by tests).

## Migration

One migration file: `backend/migrations-foundation/sql/tenant/1300.foundation.resource_model_parallel.sql`

Paired revert: `backend/migrations-foundation/sql/tenant/revert/1300.foundation.resource_model_parallel.revert.sql`.

The migration is **additive only** — no ALTER on existing tables, no
DROP, no RENAME. The existing `criteria`, `space_capabilities`,
`group_capabilities`, `space_groups`, `spaces`, `requests`,
`off_times`, `off_time_spaces` are untouched.

### Tables created

1. `resource_types` — exact DDL from `02-database-schema.md`, plus
   seed for `space` / `person` / `tool` (`is_system=true`).
2. `resources` — exact DDL from `02-database-schema.md`.
3. `resource_assignments` — exact DDL from `02-database-schema.md`.
4. `criterion_resource_types` — `(criterion_id, resource_type_id)`
   PK, FKs with `ON DELETE CASCADE`.
5. `resource_capabilities_phase1` — **Phase-1-only staging table**,
   shape identical to the final `resource_capabilities` per the schema
   doc but suffixed `_phase1`. Dropped (after data merge) in Phase 2.
6. `criteria_applicability_phase1` — single-column staging table
   `(criterion_id PK, applicable_to_requests BOOL)`. Drives the new
   service-layer validation during Phase 1; merged into
   `criteria.applicable_to_requests` in Phase 3.

The two `_phase1` tables exist so that Phase 1's service layer can be
exercised end-to-end against real data without touching the legacy
tables. They are explicitly transient.

### Backfill at the end of the migration

- Seed every existing criterion into `criterion_resource_types` as
  applicable to `space`. (This is forward-compatible with Phase 3
  which will do the same backfill for the real table.)

```sql
INSERT INTO criterion_resource_types (criterion_id, resource_type_id)
SELECT c.id, (SELECT id FROM resource_types WHERE key='space')
FROM   criteria c
ON CONFLICT DO NOTHING;
```

- No backfill into `resources` from `spaces`. That happens in Phase 2.

## Code additions

All additions live in `backend/src/`. Existing files are not edited
except for two minimal additions:

- `Program.cs` (or its endpoint registration extension methods) — add
  `app.MapResourceEndpoints()` and similar. Pure registration.
- DI container — register new services and repositories.

### Models (`backend/src/Models/`)

- `ResourceType.cs` — `record ResourceTypeInfo(Guid Id, string Key,
  string DisplayName, string? Description, bool IsSystem, bool IsActive,
  DateTime CreatedAt, DateTime UpdatedAt)`.
- `Resource.cs` — `record ResourceInfo` with all columns from
  `resources` table. `enum AllocationMode { Exclusive, Fractional,
  ConcurrentCapacity }`. `record CreateResourceRequest`,
  `record UpdateResourceRequest`.
- `ResourceAssignment.cs` — `record ResourceAssignmentInfo`,
  `enum AssignmentStatus { Planned, Confirmed, Tentative, Cancelled }`,
  `record CreateResourceAssignmentRequest`.
- `ResourceCapability.cs` — `record ResourceCapabilityInfo(Guid Id,
  Guid ResourceId, Guid CriterionId, JsonElement Value, …)`.
- `ResourceConflict.cs` — record + enum per `01-resource-domain-model.md`
  §"Conflict explanation".
- `IResourceRequirement.cs` — interface for the seam:
  ```csharp
  public interface IResourceRequirement {
    Guid ResourceTypeId { get; }
    int Count { get; }
    IReadOnlyList<Guid> RequiredCriterionIds { get; }
  }
  public sealed record SpaceResourceRequirement(
    Guid ResourceTypeId, IReadOnlyList<Guid> RequiredCriterionIds)
    : IResourceRequirement { public int Count => 1; }
  ```

### Constants (`backend/src/Constants/`)

- `ResourceTypeKeys.cs`: `Space`, `Person`, `Tool` string constants.
- `AllocationModes.cs`: `Exclusive`, `Fractional`, `ConcurrentCapacity`.
- `AssignmentStatuses.cs`: `Planned`, `Confirmed`, `Tentative`, `Cancelled`.

No string literals in business logic.

### Repositories (`backend/src/Repositories/`)

- `IResourceTypeRepository` / `ResourceTypeRepository` — read-only
  list + get-by-key.
- `IResourceRepository` / `ResourceRepository` — CRUD (soft-delete via
  `is_active`), filter by `resourceTypeKey`, `isActive`, `capability`,
  `groupId`, `search`.
- `IResourceAssignmentRepository` / `ResourceAssignmentRepository` —
  CRUD + range queries by resource + range queries by request +
  overlap detection helpers.
- `IResourceCapabilityRepository` / `ResourceCapabilityRepository` —
  reads/writes `resource_capabilities_phase1`. Configured via a single
  table-name constant so Phase 2 only changes the constant.
- `ICriterionApplicabilityRepository` /
  `CriterionApplicabilityRepository` — reads/writes
  `criterion_resource_types` and `criteria_applicability_phase1`.

All repositories use Dapper + raw SQL (project convention).

### Services (`backend/src/Services/`)

- `IResourceService` / `ResourceService`:
  - `Create / Update / Deactivate / Get / List`
  - Validates: resource_type exists, allocation_mode valid,
    base_availability_percent in [0,100], name non-empty.

- `IResourceAssignmentService` / `ResourceAssignmentService`:
  - `CreateAsync(request)`, `CancelAsync(assignmentId)`,
    `ListByRequestAsync(requestId)`,
    `ListByResourceAsync(resourceId, fromUtc, toUtc)`.
  - Validation chain (in order; first failure short-circuits and
    returns a `ResourceConflict`):
    1. Resource exists.
    2. Resource is active.
    3. Resource type matches the (Phase-1-injected) expected type, if
       the caller passed `IResourceRequirement`.
    4. Required capabilities present on the resource (Phase 1:
       presence match; Phase 3 adds typed operators). Inherited from
       `resource_group_capabilities` if not direct.
    5. Resource is not absent during requested time (joins
       `off_times` + `off_time_resources`; honors
       `applies_to_all_resources=true`).
    6. Allocation mode rules:
       - `Exclusive`: no overlapping active assignment.
       - `Fractional`: sum of overlapping `allocation_percent` plus
         the new one ≤ `base_availability_percent`. Off-time during
         the window reduces availability to 0 for the day (initial
         coarse rule; documented in `known-limitations.md`).
       - `ConcurrentCapacity`: return `InvalidAllocationMode`.
    7. `allocation_percent` valid for the mode (NULL for Exclusive;
       (0,100] for Fractional).
  - Cancelled assignments are ignored in conflict detection.

- `ICapabilityMatcher` / `CapabilityMatcher`:
  - `bool Matches(resourceCapabilities, requiredCriterionIds)`.
  - Phase 1: presence match (parity with current
    `SchedulingFeasibilityAnalyzer` behavior). Typed operators
    arrive in Phase 3.
  - Resolves group-inherited capabilities (group → resource override).

- `IOffTimeResourceQuery` / `OffTimeResourceQuery`:
  - **Read-only** join that lets Phase-1 code consume the *existing*
    `off_times` + `off_time_spaces` tables as if they were already
    `off_time_resources`. The join uses `spaces.id = resources.id`
    (which will become true in Phase 2) and short-circuits when no
    resources row exists yet (Phase 1 has no resources rows from
    Spaces yet — but the new resource_assignment tests create their
    own non-Space resources).
  - This service is what `ResourceAssignmentService` calls. In Phase
    2, its implementation is replaced with a direct read of
    `off_time_resources` (renamed table).

  Phase 1 implementation reads `off_times` only (site-level off-times
  always block matching resources at that site, when the resource is
  a Space — i.e. exists in `spaces` AND has the matching `site_id`).
  For non-Space resources created in Phase 1, off-times do not apply
  (because non-Space resources have no `site_id`). That is correct
  behavior for Phase 1 tests.

### Endpoints (`backend/src/Endpoints/`)

- `ResourceTypeEndpoints.cs` — `app.MapResourceTypeEndpoints()`:
  - `GET /api/resource-types`
  - `GET /api/resource-types/{id}`

- `ResourceEndpoints.cs` — `app.MapResourceEndpoints()`:
  - `GET /api/resources` (with query filters: `resourceTypeKey`,
    `isActive`, `search`)
  - `GET /api/resources/{id}`
  - `POST /api/resources`
  - `PUT /api/resources/{id}`
  - `DELETE /api/resources/{id}` (soft-delete: set `is_active=false`)
  - `GET /api/resources/{id}/capabilities`
  - `PUT /api/resources/{id}/capabilities`
  - `GET /api/resources/{id}/assignments`

- `ResourceAssignmentEndpoints.cs` — `app.MapResourceAssignmentEndpoints()`:
  - `GET /api/resource-assignments/{id}`
  - `POST /api/resource-assignments` (returns 201 with the
    `ResourceAssignmentInfo`, or 409 with a `ResourceConflict[]` body)
  - `DELETE /api/resource-assignments/{id}` (cancel)

- `CriterionApplicabilityEndpoints.cs` —
  `app.MapCriterionApplicabilityEndpoints()`:
  - `GET /api/criteria/{id}/applicability`
  - `PUT /api/criteria/{id}/applicability`
    body: `{ "applicableToRequests": bool, "resourceTypeKeys": string[] }`

- `PublicEndpoints.cs` — **no new public paths**. All new endpoints
  go through API-key + tenant middleware.

Authorization: admin only (use the existing authorization policy used
by `CriteriaEndpoints`).

### Frontend

No frontend changes in Phase 1. The new endpoints are consumed only
by tests during this phase.

## Tests (all must pass before merge)

Place new tests in `backend/tests/`, mirroring the source layout.

### Repository tests

- `Repositories/ResourceTypeRepositoryTests.cs`:
  - Three seeded rows exist after migration.
  - `GetByKey("space")` returns the seeded row.
  - Cannot insert a duplicate `key` (unique constraint).
- `Repositories/ResourceRepositoryTests.cs`:
  - CRUD round-trip.
  - List filters by `resourceTypeKey`, `isActive`.
  - Soft-delete sets `is_active=false`, does not remove row.
- `Repositories/ResourceAssignmentRepositoryTests.cs`:
  - CRUD round-trip.
  - Range query returns overlapping rows only.
  - Cancelled rows are excluded from active-set queries.
- `Repositories/ResourceCapabilityRepositoryTests.cs`:
  - Upsert by `(resource_id, criterion_id)`.
  - List by resource.

### Service tests

- `Services/ResourceServiceTests.cs`:
  - Create validates allocation_mode and percent range.
  - Update preserves audit timestamps.
  - Deactivate is idempotent.
- `Services/ResourceAssignmentServiceTests.cs` — **the centerpiece**:
  - **Exclusive overlap rejected** (two assignments on the same
    Exclusive resource with overlapping windows).
  - **Exclusive adjacent allowed** (10:00–12:00 then 12:00–14:00 ok).
  - **Fractional within limit** (30% + 50% on a 100% base → both
    allowed at the same time).
  - **Fractional over limit** (60% + 50% → second rejected).
  - **Fractional reduced base** (80% base, 30% + 60% → second
    rejected because 90% > 80%).
  - **Inactive resource** rejected at creation.
  - **Resource type mismatch** rejected when `IResourceRequirement`
    is provided.
  - **Capability missing** rejected with `CapabilityMissing` conflict.
  - **Capability inherited from group** allowed.
  - **Resource-level capability overrides group-level**.
  - **Cancelled assignments ignored** in conflict detection.
  - **ConcurrentCapacity returns `InvalidAllocationMode`**.
  - **Invalid allocation_percent** rejected (>100, <=0, non-null for
    Exclusive, null for Fractional).
  - **Off-time on the resource** blocks assignment with
    `ResourceAbsent` conflict (uses `OffTimeResourceQuery`).
- `Services/CapabilityMatcherTests.cs`:
  - Presence match passes when resource has the criterion.
  - Presence match fails when resource lacks the criterion.
  - Inherited group capability passes.
  - Resource-level overrides group-level (override + match).
- `Services/CriterionApplicabilityServiceTests.cs`:
  - Capability assignment to a resource of an inapplicable type is
    rejected at the service layer.
  - Applicability list round-trips via PUT/GET.

### Endpoint tests

- `Endpoints/ResourceTypeEndpointTests.cs` — happy paths only.
- `Endpoints/ResourceEndpointTests.cs` — CRUD + filter + 404 +
  authorization (non-admin gets 403).
- `Endpoints/ResourceAssignmentEndpointTests.cs`:
  - POST happy path returns 201.
  - POST overlapping returns 409 with conflict body.
  - DELETE cancels (assignment_status → Cancelled).

### Regression assertion

Add `Endpoints/Phase1RegressionTests.cs` that runs every existing
scheduling/capability test by reference (or asserts they still
exist and pass). The intent is documented in the test header:

> Phase 1 must not change the behavior of any existing Space scheduling
> or capability code path. If any existing test breaks, the offending
> Phase 1 change must be reverted before merge.

(In practice, this is enforced by the CI test run; the regression file
is a single test asserting the baseline test classes are unchanged in
file paths.)

## Definition of done

- `dotnet test` passes for `Orkyo.Foundation.Tests`.
- Phase 1 migration applies cleanly on a fresh tenant database.
- Phase 1 migration applies cleanly on a snapshot of staging data
  (no errors, no constraint violations).
- Phase 1 revert script removes all new objects cleanly (verified on
  a fresh database).
- No edits to `SchedulingEngine`, `SchedulingService`,
  `SchedulingProblemBuilder`, `SchedulingFeasibilityAnalyzer`,
  `SpaceService`, `RequestService`, `RequestRepository`,
  `SpaceCapabilityRepository`, or any existing endpoint file.
- No edits to frontend.
- New endpoints are registered and reachable; admin authorization
  enforced.
- `STATUS.md` updated as the **last** step of the PR: Phase 1 row
  → `In review` on PR open / `Merged` on merge; the Phase 1 log
  section in `STATUS.md` describes what shipped and any deviations
  from this spec; any new decisions added to the Decisions log.

## Out of scope for Phase 1

- Renaming any existing table or column.
- Dropping `requests.space_id`.
- Modifying `SchedulingEngine` or any scheduler/solver code.
- Frontend changes.
- Typed capability operators (Number ≥/≤/=, Enum membership, etc.) —
  Phase 1 is presence-match only.
- Person/Tool user-facing surfaces.
- Utilization endpoints.

## Implementation order suggested for the implementer

1. Migration + revert script.
2. Models + constants.
3. Repositories with tests.
4. `CapabilityMatcher` with tests.
5. `ResourceAssignmentService` with full validation chain + tests.
6. `ResourceService` with tests.
7. Endpoints with tests.
8. `OffTimeResourceQuery` with tests.
9. `STATUS.md` update (Phase 1 log section + phase board row).

Commit per item, reviewable. PR is the whole set.
