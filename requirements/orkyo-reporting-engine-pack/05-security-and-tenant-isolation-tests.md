# Security and Tenant Isolation Tests

## 1. Goal

Prove that embedded reports cannot leak data between tenants.

Leakage would be a critical incident. Reporting must therefore be tested like an authorization boundary, not like a cosmetic UI feature.

## 2. Core principle

No report test is complete unless it verifies negative access:

- Tenant A user must see Tenant A data.
- Tenant A user must not see Tenant B data.
- Tenant B user must see Tenant B data.
- Tenant B user must not see Tenant A data.

## 3. Seed data markers

Create test fixtures with explicit marker values.

Tenant A:

```text
TENANT_A_REPORT_MARKER_SPACE
TENANT_A_REPORT_MARKER_REQUEST
TENANT_A_REPORT_MARKER_RESOURCE
TENANT_A_REPORT_MARKER_ALLOCATION
TENANT_A_REPORT_MARKER_ABSENCE
```

Tenant B:

```text
TENANT_B_REPORT_MARKER_SPACE
TENANT_B_REPORT_MARKER_REQUEST
TENANT_B_REPORT_MARKER_RESOURCE
TENANT_B_REPORT_MARKER_ALLOCATION
TENANT_B_REPORT_MARKER_ABSENCE
```

These markers must appear in relevant report views for the owning tenant.

## 4. Backend API tests

### Test: report catalogue does not accept tenant parameter

Attempt:

```http
GET /api/reports?tenantId=<otherTenantId>
```

Expected:

- Tenant parameter ignored or rejected.
- Result is based only on authenticated tenant context.

### Test: embed token does not accept tenant parameter

Attempt:

```http
POST /api/reports/space-utilization/embed-token
{
  "tenantId": "other-tenant"
}
```

Expected:

- Request body tenant ID ignored or rejected.
- Token is issued only for authenticated tenant context.

### Test: user without permission cannot access reports

Expected:

- `GET /api/reports` returns 403 or empty list according to Orkyo convention.
- `POST /api/reports/{key}/embed-token` returns 403.

### Test: unknown report key

Expected:

- 404 or 403.
- No internal Superset IDs exposed.

## 5. Database permission tests

Using tenant reporting reader credentials:

### Must succeed

```sql
SELECT * FROM reporting.rpt_space_utilization LIMIT 1;
```

### Must fail

```sql
SELECT * FROM public.requests LIMIT 1;
SELECT * FROM public.users LIMIT 1;
CREATE TABLE reporting.bad_test(id int);
INSERT INTO reporting.some_view VALUES (...);
```

If schema-per-tenant is used, also verify:

```sql
SELECT * FROM other_tenant_reporting.rpt_space_utilization LIMIT 1;
```

Expected: permission denied.

## 6. Reporting view tests

For each reporting view:

1. Query as tenant reporting reader.
2. Assert owning tenant marker is visible.
3. Assert other tenant marker is not visible.

Views to test:

- `rpt_space_utilization`
- `rpt_request_pipeline`
- `rpt_allocation_conflicts`
- `rpt_resource_availability`
- `rpt_absence_impact`

## 7. Embedded dashboard tests

Use Playwright or equivalent E2E test.

Scenario A:

1. Login as Tenant A report user.
2. Open `/reports/space-utilization`.
3. Wait for report to render.
4. Assert Tenant A marker is visible where expected.
5. Assert Tenant B marker is not present anywhere in the page text, iframe content where accessible, downloaded data, or network payloads that test infrastructure can inspect.

Scenario B: reverse tenants.

## 8. Export tests

If exports are enabled:

1. Export report as CSV/XLSX/PDF if supported.
2. Assert owning tenant markers are present.
3. Assert other tenant markers are absent.
4. Assert export event is audit-logged.

Recommendation: disable exports in MVP until the embedded reporting path is stable.

## 9. Superset configuration tests

Verify tenant users cannot access:

- SQL Lab
- dataset editor
- datasource editor
- chart editor
- dashboard edit mode
- other tenant dashboards
- global Superset asset search

Expected:

- End users never receive direct Superset login credentials.
- Embedded role is minimal and dashboard-specific.

## 10. CI gate

Add a CI job:

```text
reporting-isolation-tests
```

This job must run before deployment to staging and production.

Failure must block deployment.

## 11. Manual release checklist

Before enabling reporting for a tenant:

- [ ] Reporting schema exists.
- [ ] Reporting views deployed.
- [ ] Reporting reader role created.
- [ ] Reporting reader role has no base table access.
- [ ] Superset datasource points only to tenant reporting boundary.
- [ ] Dashboard binding exists.
- [ ] Embed token flow tested.
- [ ] Cross-tenant marker test passed.
- [ ] Export disabled or tested.
- [ ] Audit logging active.

## 12. Severity classification

Any cross-tenant report data exposure is `Critical`.

Required response:

1. Disable affected report immediately.
2. Revoke affected embed tokens.
3. Disable affected datasource if needed.
4. Preserve logs.
5. Identify impacted tenants and data classes.
6. Patch and retest before re-enabling.
