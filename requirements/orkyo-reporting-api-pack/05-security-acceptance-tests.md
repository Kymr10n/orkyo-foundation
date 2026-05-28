# 05 — Security and Acceptance Tests

## Non-Negotiable Security Requirement

Reporting data leakage between tenants is a critical incident. The Reporting API must never rely on UI filters, BI-tool filters, or client-provided tenant identifiers for isolation.

Tenant isolation must be enforced by backend authentication, authorization, query scoping, and automated tests.

## Required Test Data

Create two test tenants:

```text
Tenant A: tenant-reporting-a
Tenant B: tenant-reporting-b
```

Seed each tenant with unmistakable marker data:

```text
Tenant A marker: TENANT_A_ONLY_REPORTING_MARKER
Tenant B marker: TENANT_B_ONLY_REPORTING_MARKER
```

Markers should appear in:

- request titles
- resource names
- space names
- allocation references
- group names where relevant

## Tenant Leakage Test Matrix

For every endpoint:

1. Call endpoint with Tenant A token.
2. Assert Tenant A marker is present where expected.
3. Assert Tenant B marker is absent.
4. Call endpoint with Tenant B token.
5. Assert Tenant B marker is present where expected.
6. Assert Tenant A marker is absent.
7. Repeat with JSON.
8. Repeat with CSV where supported.
9. Repeat with pagination.
10. Repeat with filters.
11. Repeat with sorting.
12. Repeat with `updatedSince`.

## Token Security Tests

Required:

- valid token succeeds
- invalid token fails with 401
- malformed token fails with 401
- expired token fails with 401
- revoked token fails with 401
- token hash is stored, raw token is not stored
- token secret is shown only once
- token belongs to exactly one tenant
- token without required scope fails with 403
- token cannot be used to access normal application APIs

## Parameter Abuse Tests

Required:

- `tenantId` query parameter is rejected or ignored safely
- `tenantSlug` query parameter is rejected or ignored safely
- unknown query parameters do not alter tenant scope
- SQL-like input in filters does not alter query semantics
- excessive `pageSize` is rejected
- invalid date ranges are rejected
- `from > to` is rejected

Preferred behavior: reject `tenantId` and `tenantSlug` explicitly with validation error to make misuse visible.

## CSV Tests

Required:

- CSV export uses same authorization path as JSON
- CSV export uses same tenant scope as JSON
- CSV escaping prevents malformed rows
- CSV does not include hidden/internal fields
- CSV respects date filters
- CSV respects pagination or explicit export limits

## Audit Tests

Required:

- successful request creates audit entry
- failed authentication does not log raw token
- revoked token attempt is audit logged without secret
- audit contains tenant, token prefix/id, endpoint, timestamp, status code, duration
- audit does not contain PII beyond what is explicitly approved

## Rate Limit Tests

Required:

- rate limit is enforced per reporting token
- rate limit is enforced per tenant where configured
- exceeding limit returns 429
- rate limiting does not leak tenant existence

## Acceptance Criteria

The feature is complete when:

- tenant admins can create and revoke reporting tokens
- reporting tokens are read-only and tenant-scoped
- initial reporting endpoints return stable JSON DTOs
- selected endpoints support CSV
- OpenAPI documents all reporting endpoints
- Power BI can consume at least one endpoint via Web connector
- all tenant leakage tests pass
- all token security tests pass
- no endpoint accepts client-controlled tenant context
- audit logging records reporting access
- rate limiting is active for reporting endpoints

## Explicit Non-Goals

The implementation must not add:

- embedded BI engine
- arbitrary SQL execution
- direct DB access for customers
- user-defined report designer
- cross-tenant reporting
- customer-managed datasource configuration inside Orkyo
