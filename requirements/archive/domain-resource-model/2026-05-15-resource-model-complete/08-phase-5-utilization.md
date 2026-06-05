# Phase 5 — Utilization

## Goal

Read-only utilization endpoints computed live from
`resource_assignments`, `off_time_resources`, and
`resources.base_availability_percent`. No stored utilization values.

## Endpoints

- `GET /api/resources/{id}/utilization?from=&to=&granularity=day|week|month`
- `GET /api/resource-groups/{id}/utilization?from=&to=&granularity=...`
- `GET /api/utilization?resourceTypeKey=&from=&to=&granularity=...`

Response shape:

```json
{
  "from": "2026-05-01T00:00:00Z",
  "to":   "2026-05-31T23:59:59Z",
  "granularity": "day",
  "buckets": [
    {
      "start": "2026-05-01T00:00:00Z",
      "end":   "2026-05-02T00:00:00Z",
      "allocated_percent": 75.0,
      "effective_availability_percent": 100.0,
      "is_exclusive_occupied": false
    }
  ]
}
```

## Computation rules

- **Exclusive resource:**
  - `is_exclusive_occupied = true` for any bucket overlapping an
    active assignment.
  - `allocated_percent = 100` for occupied buckets, `0` otherwise.

- **Fractional resource:**
  - For each bucket, sum overlapping active assignments'
    `allocation_percent`, time-weighted by overlap fraction.
  - `effective_availability_percent = base_availability_percent`
    reduced for any overlapping off-time (initial implementation:
    off-time blocks the whole bucket → effective = 0 for that bucket).

- **Group utilization:** average across members, weighted by member
  count. Filter members by their `is_active` flag.

## Tests

- `UtilizationServiceExclusiveTests` — occupancy boolean per bucket.
- `UtilizationServiceFractionalTests` — percent math, partial-bucket
  overlap.
- `UtilizationServiceOffTimeTests` — off-time reduces effective
  availability to 0.
- `UtilizationServiceGroupTests` — averaging across members.

## Definition of done

- All endpoints reachable, admin-authorized.
- All tests pass.
- No stored utilization field anywhere in the schema.
- `STATUS.md` updated: Phase 5 row → `Merged`; Phase 5 log section
  describes what shipped and any deviations.
