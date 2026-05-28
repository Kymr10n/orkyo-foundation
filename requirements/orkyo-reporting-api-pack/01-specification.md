# 01 — Specification: Reporting Integration API

## Context

Orkyo should not embed or operate a full reporting engine at this stage. Embedded BI increases product complexity, operational burden, tenant-provisioning complexity, upgrade surface, and security risk. The immediate customer value can be achieved more safely by exposing curated, tenant-isolated reporting endpoints.

External tools such as Power BI, Excel, Metabase, Superset, or customer-owned data platforms can consume the API.

## Core Decision

Implement a **Reporting Integration API** instead of an embedded reporting engine.

Orkyo remains responsible for:

- tenant isolation
- authorization
- stable reporting contracts
- curated operational datasets
- exportability
- auditability
- performance safeguards

Customers remain responsible for:

- dashboard creation
- BI tooling
- visual design
- downstream data modelling
- report distribution outside Orkyo

## Design Principles

1. **Tenant isolation is non-negotiable**
   - Never accept `tenantId` as a query parameter.
   - Resolve tenant from authenticated context, subdomain, API token, or service principal mapping.
   - All reporting queries must be scoped server-side.

2. **Curated datasets only**
   - Do not expose arbitrary SQL.
   - Do not expose raw application tables.
   - Expose stable DTOs backed by service-layer queries, database views, or materialized views.

3. **Operational reporting, not analytics platform**
   - Focus on data extraction and standard reporting datasets.
   - Avoid dashboard designer, embedded chart builder, semantic layer, or report scheduling in the first implementation.

4. **API-first**
   - JSON first.
   - CSV export where useful.
   - Pagination, filtering, sorting, and `updatedSince` support.
   - OpenAPI documentation.

5. **Secure by default**
   - Read-only reporting tokens.
   - Revocable API keys/service tokens per tenant.
   - Rate limiting.
   - Audit logging.
   - Least-privilege database access.

## Target Architecture

```text
External reporting client
  Power BI / Excel / Metabase / Superset / custom integration
        |
        | HTTPS + reporting token
        v
Orkyo Reporting API
        |
        | tenant resolved server-side
        v
Reporting service layer
        |
        | read-only queries / views
        v
Tenant-scoped Orkyo data
```

Recommended internal path:

```text
Controller -> ReportingAuthorization -> ReportingService -> ReportingRepository -> Reporting Views / Queries
```

## Scope: MVP

The MVP should expose read-only reporting endpoints for:

- space utilization
- resource utilization
- allocations
- requests throughput
- absences
- conflicts / overbooking
- capacity vs demand

The MVP must include:

- tenant-safe authentication
- read-only reporting token model
- token creation/revocation UI for tenant admins
- JSON responses
- CSV export for selected endpoints
- OpenAPI documentation
- audit logs
- rate limiting
- automated tenant leakage tests

## Out of Scope for MVP

Do not implement:

- embedded Superset or Metabase
- custom dashboard designer
- arbitrary SQL reporting
- user-defined report builder
- cross-tenant analytics
- scheduled report email delivery
- report sharing portal
- write-back from reporting tools
- direct database access for customers

## Multi-Tenancy Rules

The Reporting API must use the same tenant boundary as the application backend.

Allowed:

- tenant resolved from authenticated user session
- tenant resolved from subdomain
- tenant resolved from reporting API token mapped to exactly one tenant

Not allowed:

- `?tenantId=...`
- client-supplied tenant slug for authorization decisions
- BI-tool filters as tenant isolation
- shared API tokens across tenants
- exposing global IDs that allow tenant enumeration

## Data Exposure Policy

Reporting DTOs must not expose sensitive operational internals by default.

Avoid exposing:

- user authentication identifiers
- Keycloak subject IDs unless explicitly needed
- internal database primary keys where public/reporting IDs are sufficient
- security configuration
- tenant metadata of other tenants
- unpublished system settings
- secrets or integration tokens

For people/resource reporting, decide explicitly whether personal data is included. Prefer minimal fields:

- resource display name
- resource type
- group / department where relevant
- availability percentage
- allocation metrics

Avoid personal absence reasons unless required. Use absence category summaries where possible.

## Compatibility With Power BI

The API should be easy to consume from Power BI using Web connector patterns.

Prioritize:

- stable schemas
- ISO 8601 timestamps
- date range filters
- predictable pagination
- incremental refresh via `updatedSince`
- CSV fallback
- clear API documentation

Power BI-specific connector development is not required for MVP.
