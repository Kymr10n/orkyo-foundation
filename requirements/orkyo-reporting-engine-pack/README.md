# Orkyo Reporting Engine Implementation Pack

This pack defines the first implementation slice for a tenant-safe reporting engine in Orkyo.

The primary architectural goal is **tenant isolation by design**. Reports must never rely on UI filters or user-provided tenant parameters for data separation. The reporting engine is used as a controlled rendering component, not as an unrestricted self-service BI workspace.

## Files

1. `01-reporting-engine-spec.md`  
   Functional and technical specification.

2. `02-implementation-plan.md`  
   Phased implementation plan for Copilot execution.

3. `03-first-use-cases.md`  
   Initial reporting use cases and report catalogue.

4. `04-ui-integration.md`  
   Frontend integration concept for the Orkyo Reports page.

5. `05-security-and-tenant-isolation-tests.md`  
   Mandatory security controls and acceptance tests.

## Recommended engine

Default recommendation: **Apache Superset** as embedded reporting engine.

Use Superset only through tenant-scoped datasources and backend-issued embed tokens. Do not expose Superset authoring, SQL Lab, global dashboards, or cross-tenant datasets to tenant users.
