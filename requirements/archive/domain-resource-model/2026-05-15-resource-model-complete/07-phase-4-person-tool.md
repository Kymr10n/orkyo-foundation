# Phase 4 — Person + Tool Activation

## Goal

- Admin can create / edit / deactivate Person and Tool resources via
  `/api/resources` (Space is still created via `/api/sites/{siteId}/spaces`
  because Space owns site-specific attributes).
- Person resources default to `AllocationMode.Fractional`.
- Tool resources default to `AllocationMode.Exclusive`.
- Requests can require multiple resources of multiple types.
- Solvers handle multi-resource requests through `IResourceRequirement`.

## Migration

`backend/migrations-foundation/sql/tenant/1330.foundation.resource_group_members.sql`:

```sql
-- Non-Space resources need group membership too.
CREATE TABLE resource_group_members (
  resource_group_id UUID NOT NULL REFERENCES resource_groups(id) ON DELETE CASCADE,
  resource_id       UUID NOT NULL REFERENCES resources(id)       ON DELETE CASCADE,
  PRIMARY KEY (resource_group_id, resource_id)
);
```

`spaces.group_id` continues to work for Space resources (preserves the
existing FK). `resource_group_members` is the canonical membership
table for non-Space resources.

## Code changes

- New endpoints:
  - `POST /api/resources` (extended from Phase 1 read endpoint) —
    allowed for `resource_type_id ∈ (person, tool)`. Creating a
    Space via this endpoint is **rejected** (use Space endpoints).
  - `PUT /api/resources/{id}` — admin only.
  - `DELETE /api/resources/{id}` — soft-delete via `is_active=false`.
  - `GET/PUT /api/resource-groups/{id}/members`.
  - `GET /api/resources/{id}/absences`,
    `POST /api/resources/{id}/absences`,
    `PUT /api/resources/{id}/absences/{absenceId}`,
    `DELETE /api/resources/{id}/absences/{absenceId}` —
    per-resource off-times. Internally writes to `off_times` +
    `off_time_resources` with `applies_to_all_resources=false`.

- `IResourceRequirement` gains `MultiTypeResourceRequirement`:
  ```csharp
  public sealed record MultiTypeResourceRequirement(
    Guid ResourceTypeId,
    int  Count,
    IReadOnlyList<Guid> RequiredCriterionIds) : IResourceRequirement;
  ```

- Request DTO `RequestInfo` gains an optional
  `resourceRequirements: ResourceRequirementDto[]` with shape:
  ```
  { resourceTypeKey: string,
    count: int,
    requiredCriterionIds: Guid[] }
  ```
  When the field is absent, behavior is identical to today
  (single Space requirement derived from `request_requirements`).

- `SchedulingProblemBuilder` yields one `IResourceRequirement` per
  entry of `resourceRequirements[]`. Default single-Space behavior
  preserved.

- OR-Tools and Greedy solvers extended to handle multiple required
  resources per request. The exact extension is left to the
  implementer but **must keep single-Space requests byte-equivalent**
  to today (asserted by the regression baseline tests).

## Frontend

- New "Resources" admin area:
  - List page (filter by type).
  - Create / Edit form for Person and Tool.
  - Capability editor (reuses the resource_capabilities endpoint).
  - Absence editor (uses the new per-resource off-time endpoints).
- Existing Space admin screens unchanged.
- Request edit screen: future enhancement to set
  `resourceRequirements[]` is out of scope for Phase 4 (the contract
  exists; UI lands in a follow-on initiative).

## Tests

- `PersonResourceAllocationTests` — fractional behavior end-to-end.
- `ToolResourceAllocationTests` — exclusive behavior end-to-end.
- `MultiResourceRequirementTests` — request requires one Space +
  one Person; both are matched and assigned.
- `ResourceGroupMembershipTests`.
- `ResourceAbsenceEndpointTests`.

## Definition of done

- Migration applies; revert clean.
- All existing tests pass; single-Space requests byte-equivalent
  to pre-phase behavior (regression baseline).
- Admin can create Person and Tool resources end-to-end via UI.
- Multi-resource requests work via API (UI follow-on tracked).
- `STATUS.md` updated: Phase 4 row → `Merged`; Phase 4 log section
  describes what shipped and any deviations.
