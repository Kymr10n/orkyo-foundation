# Phase 3 — Criterion Applicability Tagging

## Goal

- Add `criteria.applicable_to_requests`.
- The `criterion_resource_types` table created in Phase 1 is now the
  authoritative source (Phase 1 already seeded it for type=space).
- Service-layer validation rejects mismatched assignments.
- `CapabilityMatcher` gains typed operator support.

## Migration

`backend/migrations-foundation/sql/tenant/1320.foundation.criteria_applicability.sql`:

```sql
ALTER TABLE criteria
  ADD COLUMN applicable_to_requests BOOLEAN NOT NULL DEFAULT true;
```

The `criterion_resource_types` table already exists and is populated
from Phase 1. No data migration needed.

Revert script drops the column.

## Code changes

- `CriteriaService` enforces:
  - When attaching a capability to a Resource: the criterion must be
    in `criterion_resource_types(criterion_id, resource_type_id)`
    for the resource's type. Otherwise return
    `CapabilityNotApplicable` conflict / 400 response.
  - When attaching a requirement to a Request: `criteria.applicable_to_requests`
    must be `true`.
- `CapabilityMatcher` extends presence-match with typed operators:

  | `data_type` | Match rule                                                  |
  |-------------|-------------------------------------------------------------|
  | Boolean     | resource value is `true`.                                   |
  | Number      | resource value satisfies request operator (≥/≤/=).          |
  | Enum        | resource value ∈ request's allowed set.                     |
  | String      | resource value equals request value (case-insensitive).     |

  `RequestRequirementInfo` gains an optional `operator` field (Number
  only) and an optional `allowed_values` field (Enum only). When
  absent, falls back to presence-match (backward compatible).
- New endpoints:
  - `GET /api/criteria/{id}/applicability` → returns
    `{ applicableToRequests, resourceTypeKeys: [] }`.
  - `PUT /api/criteria/{id}/applicability` → updates both.

## Tests

- `CriterionApplicabilityTests`:
  - Capability assignment to a resource of an inapplicable type → 400.
  - Requirement creation against a criterion with
    `applicable_to_requests=false` → 400.
- `CapabilityMatcherTypedTests`:
  - Number ≥ operator: resource 800kg ≥ request 500kg → match.
  - Number = operator strict.
  - Enum membership.
  - Boolean true vs false.
  - String case-insensitive equality.

## Definition of done

- Migration applies; revert clean.
- All existing tests pass.
- New typed-match behavior covered by tests.
- Frontend can read/write applicability via the new endpoints (UI may
  ship later — the contract must exist).
- `STATUS.md` updated: Phase 3 row → `Merged`; Phase 3 log section
  describes what shipped and any deviations.
