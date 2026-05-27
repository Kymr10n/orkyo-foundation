# Reporting Engine Implementation Plan

## Phase 0 — Design guardrails

Before implementation, confirm these architectural decisions in code comments and documentation:

- Reporting engine is not the tenant isolation authority.
- Tenant isolation is enforced through datasource/database role/backend authorization.
- UI filters are usability features only, never security controls.
- No tenant ID is accepted from the browser for report access.
- No tenant user receives direct BI-engine credentials.

## Phase 1 — Backend report catalogue

Create a backend report catalogue independent from Superset.

Tasks:

1. Add report permission constants:
   - `reports.view`
   - `reports.export`
   - `reports.admin`

2. Add report domain models:
   - `ReportDefinition`
   - `TenantReportBinding`
   - `ReportCategory`
   - `ReportEmbedTokenResult`

3. Add service interfaces:

```csharp
public interface IReportCatalogService
{
    Task<IReadOnlyList<ReportDefinition>> GetVisibleReportsAsync(CurrentUserContext user, CancellationToken ct);
}

public interface IReportEmbedService
{
    Task<ReportEmbedTokenResult> CreateEmbedTokenAsync(CurrentUserContext user, string reportKey, CancellationToken ct);
}
```

4. Add API controller:

```http
GET /api/reports
POST /api/reports/{reportKey}/embed-token
```

5. Ensure backend resolves tenant from authenticated session/context only.

Acceptance criteria:

- User without `reports.view` sees no reports or gets 403.
- User cannot request report for another tenant.
- Unknown report key returns 404 or 403 without revealing tenant internals.

## Phase 2 — Reporting schema and views

Add database migration for reporting schema/views.

Tasks:

1. Create `reporting` schema.
2. Create first reporting views:
   - `rpt_space_utilization`
   - `rpt_request_pipeline`
   - `rpt_allocation_conflicts`
   - `rpt_resource_availability`
3. Avoid exposing sensitive columns.
4. Add indexes on base tables where needed.
5. Consider materialized views only after performance validation.

Acceptance criteria:

- Views can be queried by reporting reader role.
- Reporting reader role cannot select from app/base schemas.
- Views return only tenant-local data because they are deployed inside tenant DB/schema.

## Phase 3 — Reporting DB user provisioning

Add provisioning logic for a per-tenant reporting reader.

Tasks:

1. Generate tenant-specific reporting username.
2. Generate strong managed password.
3. Store credentials in the existing secret mechanism, not in app tables as plaintext.
4. Grant only `CONNECT`, `USAGE` on reporting schema, and `SELECT` on reporting views.
5. Add rotation capability.

Acceptance criteria:

- Reporting role cannot create tables.
- Reporting role cannot access other schemas.
- Reporting role cannot access other tenant databases/schemas.
- Credential rotation does not break existing tenant app functionality.

## Phase 4 — Superset integration service

Implement Superset as an adapter behind an interface.

Tasks:

1. Add config section:

```json
"Reporting": {
  "Engine": "Superset",
  "BaseUrl": "http://superset:8088",
  "AdminUsername": "...",
  "AdminPasswordSecretRef": "...",
  "EmbedTokenTtlSeconds": 300
}
```

2. Add interface:

```csharp
public interface IReportingEngineClient
{
    Task<ReportingDashboardBinding> EnsureDashboardBindingAsync(TenantContext tenant, string reportKey, CancellationToken ct);
    Task<ReportEmbedTokenResult> CreateGuestTokenAsync(TenantContext tenant, string reportKey, CancellationToken ct);
}
```

3. Implement `SupersetReportingEngineClient`.
4. Keep Superset-specific DTOs internal to adapter.
5. Add resilient HTTP client handling and structured logs.

Acceptance criteria:

- Backend can request an embed token for a provisioned tenant report.
- Superset API credentials are never exposed to frontend.
- Embed token TTL is short.
- Failed Superset calls return controlled errors.

## Phase 5 — Tenant report provisioning

Implement provisioning command/job.

Tasks:

1. Add application command:

```bash
orkyo reporting provision --tenant <tenant-slug>
```

or backend internal job/service method.

2. Provision datasource, datasets, and dashboard bindings.
3. Store `TenantReportBinding` in control plane or tenant metadata depending on current architecture.
4. Make provisioning idempotent.
5. Add repair/reconcile mode.

Acceptance criteria:

- Running provisioning twice does not duplicate assets.
- Removing and recreating a tenant report binding is supported.
- Failed provisioning leaves clear diagnostics.

## Phase 6 — Frontend Reports page

Add a Reports section to the Orkyo UI.

Tasks:

1. Add route: `/reports`.
2. Add navigation item: `Reports`.
3. Add report catalogue query using React Query.
4. Show report cards grouped by category.
5. Open selected report in embedded viewer.
6. Request embed token from backend only when opening a report.
7. Handle loading, no permission, empty state, and failure states.

Acceptance criteria:

- Reports page is hidden or shows access denied for users without permission.
- Tenant users only see reports enabled for their tenant.
- Browser never sends tenant ID when asking for reports.

## Phase 7 — Security and leakage test automation

Create automated tests using two seeded tenants.

Tasks:

1. Seed Tenant A with marker data: `TENANT_A_REPORT_MARKER`.
2. Seed Tenant B with marker data: `TENANT_B_REPORT_MARKER`.
3. Request Tenant A reports as Tenant A user.
4. Verify Tenant B marker never appears in:
   - report API payloads
   - embed token payloads where inspectable
   - rendered dashboard data where testable
   - exports if enabled
5. Repeat in reverse.

Acceptance criteria:

- Any leakage test failure blocks CI.
- Leakage tests run before production deployment.
- Test markers cover requests, spaces, resources, allocations, absences, and conflicts.

## Phase 8 — Operational hardening

Tasks:

1. Add health checks for Superset connectivity.
2. Add dashboard provisioning status check.
3. Add logging and audit events.
4. Add backup/restore consideration for Superset metadata DB.
5. Add dashboard-as-code or export/import workflow for dashboard templates.

Acceptance criteria:

- Orkyo can detect reporting engine outage.
- Tenant app remains usable if reporting is unavailable.
- Reporting failure does not impact core scheduling workflows.
