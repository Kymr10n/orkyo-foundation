# 05 — Acceptance Criteria

## Functional Acceptance Criteria

### Criteria Registry

- Criteria can be created with one applicability value.
- Criteria can be created with multiple applicability values.
- Criteria cannot be created without at least one applicability value.
- Criteria cannot be created with an unknown applicability value.
- Criteria can be edited to add or remove applicability values.
- Criteria list displays applicability badges.
- Settings Criteria page supports filtering by:
  - All
  - Spaces
  - People
  - Tools

### Backend API

- `GET /criteria` returns all criteria including `appliesTo`.
- `GET /criteria?appliesTo=spaces` returns only space-applicable criteria.
- `GET /criteria?appliesTo=people` returns only people-applicable criteria.
- `GET /criteria?appliesTo=tools` returns only tool-applicable criteria.
- Invalid applicability filters return `400 Bad Request`.
- Create/update payloads persist applicability correctly.

### Migration

- Existing criteria are backfilled as applicable to Spaces.
- Existing space capability workflows continue to work after migration.
- Existing request/utilization validation does not break.

### Resource-Domain Consumption

- Spaces workflows only show criteria applicable to Spaces.
- People workflows only show criteria applicable to People.
- Tools workflows, when implemented, only show criteria applicable to Tools.
- Criteria with multiple applicability values appear in all relevant resource-domain views.

## Non-Functional Acceptance Criteria

- No duplicated criterion registries are introduced.
- No hardcoded string literals are scattered across the codebase; use central constants/enums/types.
- Backend validation is performed at the request boundary.
- Frontend validation prevents empty applicability submission.
- Existing styling and dark-mode behavior remain consistent.
- Implementation follows existing repository architecture and naming conventions.
- No unrelated refactoring is performed.

## Test Coverage Expectations

Backend:

- Unit tests for validation.
- API/integration tests for create/update/list/filter behavior.
- Migration test or equivalent verification for backfill behavior.

Frontend:

- Component/form tests for create/edit dialog.
- Filtering tests for Settings Criteria page.
- Selector tests for resource-domain filtered criteria.

## Manual Verification Checklist

1. Open Settings → Criteria.
2. Create `capacity_persons` as Number, applies to Spaces.
3. Create `forklift_certified` as Boolean, applies to People.
4. Create `power_400v` as Boolean, applies to Spaces and Tools.
5. Verify All shows all three.
6. Verify Spaces shows `capacity_persons` and `power_400v`.
7. Verify People shows `forklift_certified` only.
8. Verify Tools shows `power_400v` only.
9. Verify Spaces capability selector does not show `forklift_certified`.
10. Verify People skill selector does not show `capacity_persons`.
