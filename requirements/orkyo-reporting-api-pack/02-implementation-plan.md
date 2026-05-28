# 02 — Implementation Plan

## Phase 0 — Replace Previous Direction

1. Do not implement embedded reporting engine provisioning.
2. Do not integrate Superset or Metabase into the application runtime.
3. Keep any previous reporting-engine specification only as archived research.
4. Rename the feature direction to **Reporting Integration API**.

## Phase 1 — Domain and Security Foundation

### Backend

Create a dedicated reporting area in the API project:

```text
src/Orkyo.Api/Reporting/
  Controllers/
  Contracts/
  Services/
  Authorization/
  Repositories/
  Export/
```

Add entities or tables for reporting access tokens:

```text
ReportingApiToken
- Id
- TenantId
- Name
- TokenHash
- Prefix
- CreatedAtUtc
- CreatedByUserId
- LastUsedAtUtc
- ExpiresAtUtc nullable
- RevokedAtUtc nullable
- RevokedByUserId nullable
- Scopes
```

Token requirements:

- Store only hashed token values.
- Show full token only once at creation.
- Token belongs to exactly one tenant.
- Token is read-only.
- Token supports expiry and revocation.
- Token usage is audit logged.

Suggested token format:

```text
orkyo_rpt_<public-prefix>_<secret>
```

Use the prefix for lookup and the hash for verification.

### Authorization

Implement a reporting authentication handler or middleware that:

1. Reads token from `Authorization: Bearer <token>`.
2. Validates prefix and hash.
3. Resolves exactly one tenant.
4. Sets tenant context internally.
5. Applies reporting scopes.
6. Rejects revoked or expired tokens.

Also support normal authenticated admin sessions for testing endpoints from the app UI if useful.

## Phase 2 — Reporting Query Layer

Create read-only query services.

Recommended pattern:

```csharp
public interface IReportingQueryService
{
    Task<PagedResult<SpaceUtilizationReportRow>> GetSpaceUtilizationAsync(
        ReportingQueryContext context,
        SpaceUtilizationReportQuery query,
        CancellationToken cancellationToken);
}
```

`ReportingQueryContext` must contain server-resolved tenant information. It must never be populated from client request body/query tenant identifiers.

Use one of these approaches:

1. Service-layer LINQ queries with mandatory tenant scoping.
2. Database views per tenant DB/schema.
3. Materialized reporting views for expensive aggregations.

For MVP, service-layer queries are acceptable if performance is reasonable. For frequently used metrics, add reporting views later.

## Phase 3 — REST Endpoints

Implement endpoints under:

```text
/api/reporting/v1/...
```

Initial endpoints:

```text
GET /api/reporting/v1/spaces/utilization
GET /api/reporting/v1/resources/utilization
GET /api/reporting/v1/allocations
GET /api/reporting/v1/requests/throughput
GET /api/reporting/v1/absences
GET /api/reporting/v1/conflicts
GET /api/reporting/v1/capacity-vs-demand
```

Common query parameters:

```text
from=2026-01-01T00:00:00Z
to=2026-01-31T23:59:59Z
updatedSince=2026-01-01T00:00:00Z
page=1
pageSize=500
sort=...
format=json|csv
```

Do not add `tenantId`.

Default max `pageSize`: 500 or 1000.
Absolute max `pageSize`: 5000.

## Phase 4 — CSV Export

Add CSV support for selected endpoints.

Accept either:

```text
GET /api/reporting/v1/allocations?format=csv
```

or content negotiation:

```text
Accept: text/csv
```

CSV export must obey the same authorization, filtering, pagination, and tenant isolation as JSON.

## Phase 5 — Admin UI

Add a tenant admin section:

```text
Settings -> Integrations -> Reporting API
```

Capabilities:

- Create reporting token
- Name token
- Set expiry date
- Copy token once
- View token prefix
- View created date
- View last used date
- Revoke token
- Link to API documentation
- Show sample Power BI connection instructions

Do not display token secrets after creation.

## Phase 6 — OpenAPI and Documentation

Add OpenAPI tags:

```text
Reporting API
Reporting API Tokens
```

Document:

- authentication model
- sample requests
- pagination
- date filters
- CSV usage
- Power BI guidance
- tenant isolation statement
- rate limits

## Phase 7 — Rate Limiting and Audit Logging

Apply rate limits specifically to reporting endpoints.

Suggested starting point:

- 60 requests/minute per reporting token
- 10 concurrent requests per tenant
- tighter limits for CSV exports if needed

Audit log fields:

```text
TenantId
TokenId
TokenPrefix
Endpoint
QueryHash or normalized query summary
ResponseRowCount
StatusCode
DurationMs
TimestampUtc
CallerIpHash optional
```

Do not log full token values.

## Phase 8 — Test Suite

Add automated tests for:

- token validation
- revoked token rejection
- expired token rejection
- token cannot access another tenant
- all reporting endpoints enforce tenant scoping
- no endpoint accepts `tenantId`
- CSV matches JSON tenant scope
- pagination does not leak data
- filters do not leak data
- OpenAPI generated successfully

Use two tenants with marker data:

```text
Tenant A marker: TENANT_A_ONLY_REPORTING_MARKER
Tenant B marker: TENANT_B_ONLY_REPORTING_MARKER
```

Every endpoint must be tested with both tokens.
