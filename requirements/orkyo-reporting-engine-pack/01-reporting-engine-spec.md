# Orkyo Reporting Engine Specification

## 1. Purpose

Introduce a simple reporting engine for Orkyo built on top of an existing open-source reporting/BI engine, with tenant isolation as the non-negotiable primary design constraint.

The reporting capability shall provide curated operational dashboards for site, space, people, tools, requests, allocations, capacity, availability, conflicts, and utilization.

The first target engine is Apache Superset, embedded into Orkyo through a backend-controlled token flow.

## 2. Non-negotiable security principle

Reports must never rely on frontend filters, dashboard parameters, URL parameters, user input, or BI-tool UI configuration as the only tenant boundary.

Tenant isolation must be enforced before data reaches the reporting engine.

Required isolation layers:

1. Orkyo authenticated session resolves tenant context.
2. Backend authorizes report access for the current tenant.
3. Reporting datasource is tenant-scoped.
4. Reporting database role is read-only.
5. Reporting role has access only to approved reporting views/materialized views.
6. Embedded report token grants access only to the selected dashboard for the current tenant.
7. End users cannot access ad-hoc SQL, dataset editing, datasource editing, or dashboard authoring.

## 3. Target architecture

```text
Browser
  |
  | Orkyo Reports Page
  v
Orkyo Backend API
  |
  | Resolve authenticated user + tenant membership
  | Authorize report key
  | Resolve tenant reporting datasource/dashboard mapping
  | Request BI guest/embed token
  v
Reporting Engine API / Superset
  |
  | Uses tenant-scoped datasource
  v
Postgres reporting schema / reporting DB
  |
  | Read-only views/materialized views only
  v
Tenant application data
```

## 4. Reporting data boundary

Do not let the reporting engine query Orkyo application tables directly.

Create a reporting boundary using one of these models:

### Preferred for Orkyo SaaS

Tenant database per tenant, with a reporting schema in each tenant database:

```text
tenant_db_x
  app tables
  reporting schema
    rpt_space_utilization
    rpt_request_pipeline
    rpt_resource_capacity
```

Superset datasource for tenant X connects to `tenant_db_x` using `tenant_x_reporting_reader`.

### Acceptable alternative

Tenant schema per tenant inside a shared Postgres instance:

```text
postgres
  tenant_x.app tables
  tenant_x_reporting.views
```

The reporting role must have access only to the tenant-specific reporting schema.

### Avoid

Shared tables with `tenant_id` filters as the reporting isolation mechanism.

This is not sufficient for customer-facing embedded reports because dashboard filters, SQL definitions, export paths, drilldowns, and misconfigured datasets can leak data.

## 5. Reporting database roles

For each tenant, create a dedicated read-only reporting role.

Example role concept:

```sql
CREATE ROLE tenant_abc_reporting_reader LOGIN PASSWORD 'managed-secret';
REVOKE ALL ON DATABASE tenant_abc FROM PUBLIC;
GRANT CONNECT ON DATABASE tenant_abc TO tenant_abc_reporting_reader;
GRANT USAGE ON SCHEMA reporting TO tenant_abc_reporting_reader;
GRANT SELECT ON ALL TABLES IN SCHEMA reporting TO tenant_abc_reporting_reader;
ALTER DEFAULT PRIVILEGES IN SCHEMA reporting
  GRANT SELECT ON TABLES TO tenant_abc_reporting_reader;
```

The role must not have:

- write permissions
- access to app schemas or base tables
- permission to create objects
- permission to access other tenant schemas/databases

## 6. Reporting views

Reporting views are stable product contracts. They should hide internal table complexity and expose only safe fields.

Initial reporting views:

- `reporting.rpt_space_utilization`
- `reporting.rpt_resource_utilization`
- `reporting.rpt_request_pipeline`
- `reporting.rpt_allocation_conflicts`
- `reporting.rpt_resource_availability`
- `reporting.rpt_absence_impact`

Guidelines:

- Use business-friendly column names where practical.
- Avoid exposing internal IDs unless required for drilldown.
- Prefer stable public identifiers or display names.
- Exclude secrets, auth data, technical tokens, internal audit payloads, and unnecessary PII.
- Include time grain fields where useful: day, week, month, year.
- Include site/resource/request dimensions needed for filtering.

## 7. Report catalogue model

Orkyo should maintain its own report catalogue instead of relying on the BI engine as the source of truth.

Suggested backend model:

```csharp
public sealed class ReportDefinition
{
    public string Key { get; init; } = default!;
    public string Title { get; init; } = default!;
    public string Description { get; init; } = default!;
    public string Category { get; init; } = default!;
    public bool IsEnabled { get; init; }
    public IReadOnlySet<string> RequiredPermissions { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> SupportedResourceTypes { get; init; } = new HashSet<string>();
}
```

Tenant-specific runtime mapping:

```csharp
public sealed class TenantReportBinding
{
    public Guid TenantId { get; init; }
    public string ReportKey { get; init; } = default!;
    public string SupersetDashboardId { get; init; } = default!;
    public string SupersetDatasetId { get; init; } = default!;
    public string SupersetDatasourceId { get; init; } = default!;
    public bool IsProvisioned { get; init; }
}
```

## 8. Backend API

Initial API surface:

```http
GET /api/reports
```

Returns the report catalogue visible to the current user and tenant.

```http
POST /api/reports/{reportKey}/embed-token
```

Returns an embed token or embed payload for the requested report, scoped to the authenticated user's current tenant.

The client must not pass tenant ID. Tenant is resolved server-side.

## 9. Permissions

Add report permissions to the existing authorization model:

- `reports.view`
- `reports.export` optional
- `reports.admin` internal/admin only

Tenant users may view reports only if they have `reports.view` and the report is enabled for their tenant.

## 10. Superset operating model

Superset shall be operated as an internal service.

Tenant users do not log into Superset directly.

Disable or hide from tenant users:

- SQL Lab
- dataset editor
- datasource editor
- chart editor
- dashboard editing
- global search across assets
- cross-dashboard navigation that bypasses Orkyo

Use Superset embedding with backend-issued guest tokens. Guest-token creation must happen only in the Orkyo backend.

## 11. Provisioning

Tenant onboarding must include reporting provisioning:

1. Create reporting schema/views/materialized views for tenant.
2. Create or rotate tenant reporting DB user.
3. Register tenant datasource in Superset.
4. Register datasets based on reporting views.
5. Attach tenant dashboard copies or parameterized dashboard templates to tenant datasource.
6. Store `TenantReportBinding` records in Orkyo control plane.
7. Run leakage smoke tests.

For Community Edition, provisioning can be static and single-tenant.

## 12. Exports

Exports are high-risk for leakage and should initially be disabled or limited.

If export is enabled:

- Export must use the same tenant-scoped datasource.
- Export permission must be explicit.
- Export actions must be logged in Orkyo audit trail.
- Export must be tested for cross-tenant leakage.

## 13. Audit logging

Log these events in Orkyo:

- report catalogue viewed
- report embed token issued
- report opened
- report export requested
- report provisioning changed
- report access denied

Minimum audit fields:

- timestamp
- tenant ID
- user ID
- report key
- action
- result
- source IP / request metadata where available

## 14. Out of scope for first release

- End-user report designer
- Ad-hoc SQL
- Cross-tenant benchmarking
- Customer-defined calculated metrics
- Writeback from reports
- Complex semantic model
- Public report sharing
