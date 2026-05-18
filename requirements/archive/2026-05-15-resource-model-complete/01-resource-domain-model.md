# Resource Domain Model

## Mental model

Old:
```
Request → Space
```

New:
```
Request → ResourceAssignment(s) → Resource
                                     │
                                     ├── ResourceType = space  → spaces extension row
                                     ├── ResourceType = person
                                     └── ResourceType = tool
```

A Space is a Resource of type `space`. A Person is a Resource of type
`person`. The `spaces` table continues to exist to hold Space-specific
attributes (site, geometry, capacity, group, code, properties) but its
primary key is also a foreign key into `resources`.

## Entities

### ResourceType
- `key` (stable identifier): `space`, `person`, `tool`. Seed only.
- `is_system = true` for the three seeded types; no UI to create custom
  types in this initiative.

### Resource
- `name`, `description`, `external_reference`
- `resource_type_id` → ResourceType
- `allocation_mode`: `Exclusive` | `Fractional` | `ConcurrentCapacity`
- `base_availability_percent` ∈ [0, 100], default 100
- `is_active` (soft-delete pattern; never hard-delete historical data)
- `metadata_json` (per-type extension; not indexed initially)

### Space (subtype-extension of Resource)
- `id = resources.id` (shared uuid; FK with `ON DELETE RESTRICT`)
- Retains existing columns: `site_id`, `code`, `is_physical`,
  `geometry`, `properties`, `group_id`, `capacity`
- `name` and `description` are removed from `spaces` (they live in
  `resources`; `spaces` joins to read them)

### Criterion (kept as `criteria`)
- Existing columns kept: `name`, `data_type` (Boolean/Number/String/Enum),
  `enum_values`, `unit`
- New: `applicable_to_requests BOOLEAN NOT NULL DEFAULT true`
- New M:N table `criterion_resource_types(criterion_id, resource_type_id)`
  declares which resource types a criterion may be assigned to.
- A criterion can act as a Capability (attached to a Resource via
  `resource_capabilities`) and/or as a Requirement (attached to a
  Request via `request_requirements`).

### ResourceCapability (renamed from `space_capabilities`)
- `(resource_id, criterion_id) → value JSONB`
- Value typing matches the criterion's `data_type`.

### ResourceGroup (renamed from `space_groups`)
- Adds `resource_type_id` (NOT NULL; backfilled to `space`).
- Group capabilities table renamed from `group_capabilities` to
  `resource_group_capabilities`.
- Membership: `spaces.group_id` is renamed/repurposed; for non-space
  resource types, membership lives via a new `resource_group_members`
  table introduced in Phase 4.

### ResourceAssignment
- `(request_id, resource_id, start_utc, end_utc, allocation_percent,
   allocation_units, assignment_status)`
- `assignment_status`: `Planned` | `Confirmed` | `Tentative` | `Cancelled`
- Only `planning_mode='leaf'` requests own assignments. Summary /
  container requests roll up read-only.
- Unique constraint `(request_id, resource_id) WHERE status != 'Cancelled'`
  prevents duplicate active assignments to the same resource.

### OffTime (kept; per-resource scoping generalized)
- `off_time_spaces` renamed to `off_time_resources` (column
  `space_id` → `resource_id`).
- `off_times.applies_to_all_spaces` renamed to
  `applies_to_all_resources`.
- An off-time with `applies_to_all_resources=true` blocks every
  resource at the site (current site-wide behavior preserved).
- An off-time with `applies_to_all_resources=false` blocks only the
  resources listed in `off_time_resources`.
- Recurring rules (`is_recurring`, `recurrence_rule`) are stored but
  not expanded — same behavior as today.
- A bug in the current `SchedulingEngine` (it ignores
  `applies_to_all_spaces`) is fixed at cutover: both
  `SchedulingEngine` and `SchedulingFeasibilityAnalyzer` go through
  a shared helper.

## Allocation modes

### Exclusive
A resource can be assigned to at most one request in any given time
window. Used by Spaces and most Tools. Implementation rule:

```
For an Exclusive resource R, no two ResourceAssignments may have
overlapping [start_utc, end_utc) windows when their status is in
(Planned, Confirmed, Tentative).
```

### Fractional
A resource can be partially allocated by percentage. Used by Persons.
Implementation rule:

```
For a Fractional resource R at any instant t inside [start, end):
sum(allocation_percent of overlapping active assignments at t)
  <= R.base_availability_percent - reduction_from_overlapping_off_times(t)
```

If partial-day off-times are too complex initially, treat any
overlapping off-time as full unavailability for the day. Record the
limitation in the "Open issues / deviations" section of `STATUS.md`.

### ConcurrentCapacity
Reserved for future use (parking pools, generic workstations). Schema
permits it; scheduler does not implement it in this initiative. Any
attempt to assign to a ConcurrentCapacity resource returns
`InvalidAllocationMode`.

## Capability matching

Implemented by a single service `CapabilityMatcher`. Inputs:

- A request's `RequiredCriterionIds` (with optional value constraints
  in future versions; initial implementation = presence match).
- A resource's effective capabilities, computed as:
  ```
  resource_capabilities  ∪  inherited resource_group_capabilities
  ```
  with resource-level capability values overriding group-level when
  both exist.

Typed matching extends the current presence-based logic:

| `data_type` | Match rule                                                    |
|-------------|---------------------------------------------------------------|
| Boolean     | Resource has a `true` value.                                  |
| Number      | Resource value satisfies request value with operator (≥, ≤, =). |
| Enum        | Resource value is in the request's allowed set.               |
| String      | Resource value equals request value (case-insensitive).       |

For Phase 1, only presence-match is required (parity with today's
behavior at `SchedulingFeasibilityAnalyzer.Analyze()`). Typed
operators ship in Phase 3 alongside applicability tagging.

## Conflict explanation

The scheduler returns structured conflicts instead of booleans:

```csharp
record ResourceConflict(
  Guid ResourceId,
  ResourceConflictType Type,
  string Message,
  Guid? ConflictingAssignmentId,
  Guid? ConflictingOffTimeId);

enum ResourceConflictType {
  ResourceInactive,
  CapabilityMissing,
  CapabilityNotApplicable,
  ResourceAbsent,
  ExclusiveOverlap,
  FractionalCapacityExceeded,
  InvalidAllocationMode,
  InvalidAllocationPercent
}
```

This is consumed by the auto-schedule UI and by validation errors on
manual assignment endpoints.

## Invariants

These are enforced at the service layer and asserted by tests.

1. Every Space has exactly one `resources` row with the same id.
2. A `resources` row of type=space without a matching `spaces` row is
   illegal (enforced by Phase 2 migration and by `SpaceService`).
3. A capability cannot be attached to a resource whose type is not in
   `criterion_resource_types(criterion_id)`.
4. A requirement cannot reference a criterion with
   `applicable_to_requests=false`.
5. Only `planning_mode='leaf'` requests own `resource_assignments`.
6. Active assignments are unique per `(request_id, resource_id)`.
7. Inactive resources cannot receive new assignments.
8. `Exclusive` resources cannot have overlapping active assignments.
9. `Fractional` resources cannot exceed their effective availability.
10. `off_times.applies_to_all_resources=false` requires at least one
    row in `off_time_resources` referencing the off-time.
