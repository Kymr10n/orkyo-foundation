# 06 — Validation, Security, Performance, and Correctness

## Tenant Isolation

Mandatory rules:

- Do not accept client-provided tenant context as authoritative.
- Derive tenant from authenticated session/token/server context.
- All analytics views must include `tenant_id`.
- All repository queries must filter by tenant.
- Site filters must be checked against the tenant.
- No cross-tenant aggregation in built-in product dashboards.

## Data Leakage Prevention

Avoid exposing:

- Other tenants' resource names
- Other tenants' site IDs
- Raw request descriptions in analytics endpoints
- User/person-level utilization unless explicitly authorized

Initial dashboards should show aggregate metrics by tenant/site/resource type, not individual people, unless an existing permission model already supports that.

## Date Range Validation

Prevent unbounded queries.

Recommended validation:

```text
from required
to required
from < to
bucket required for trend endpoints
range limit based on bucket
```

## Capacity Correctness

The biggest risk is misleading utilization.

Document clearly whether capacity means:

- Working-time capacity
- Calendar availability capacity
- Occupancy time
- Area-time capacity
- Headcount capacity

Initial recommendation:

```text
People = available working minutes
Spaces = available/open minutes
Tools = available/open minutes, if implemented
```

Do not mix incompatible units.

## Conflict Correctness

If conflicts are currently calculated live and not persisted, choose one:

1. Reuse existing validation logic through a backend service and expose aggregated result.
2. Persist conflict states as part of scheduling/validation workflow.
3. Start with request-level conflict flags if detailed conflict types are unavailable.

Preferred long-term direction: persist conflict records or deterministic validation results, because analytics over conflicts should not depend on expensive repeated recalculation.

## Performance Rules

Initial live views are acceptable if:

- Date ranges are bounded.
- Views are indexed through underlying tables.
- Queries are tenant-filtered.
- Response payloads are aggregated, not raw records.

When to move to snapshot/materialized data:

- Dashboard queries exceed acceptable latency.
- Live conflict recalculation becomes expensive.
- Tenant data grows significantly.
- Power BI consumers increase load.
- Historical metrics must remain stable even after request/resource changes.

## Testing Strategy

### Backend unit tests

- Bucket parsing
- Date validation
- Status mapping
- Conflict type mapping
- Utilization percentage calculation

### Backend integration tests

Seed:

- Two tenants
- Multiple sites
- Spaces and people
- Scheduled and unscheduled requests
- Overbooking conflict
- Criteria mismatch conflict
- Resource unavailable conflict

Assert:

- Tenant A cannot see Tenant B
- Site filter works
- Empty periods return zeroes or empty arrays consistently
- Utilization values are correct
- Conflict totals match seeded data

### Frontend tests

- KPI cards render
- Charts render with API data
- Empty states render
- Filter changes update requests
- Error handling is visible but not noisy

## Observability

Log backend query failures with:

- endpoint
- tenant id
- site id
- bucket
- date range
- correlation id

Do not log sensitive request details.
