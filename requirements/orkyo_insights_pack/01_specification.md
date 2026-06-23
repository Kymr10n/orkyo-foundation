# 01 — Specification: Built-in Insights Semantic Layer

## Objective

Provide high-value built-in operational insights in Orkyo without building a general-purpose reporting engine.

The solution shall provide curated dashboards for:

- Resource utilization for spaces and people
- Total capacity vs. used capacity by time bucket
- Request status totals and trends
- Scheduled vs. unscheduled requests
- Conflict totals and trends
- Conflict breakdown by type, especially overbooking and criteria mismatch

Advanced reporting, ad-hoc analysis, custom reports, and BI modelling remain outside the product and are supported via APIs / Power BI integration.

## Architectural Principle

Introduce a stable analytics semantic layer in the database:

```text
Operational domain tables
        ↓
analytics_* views
        ↓
Insights backend repository
        ↓
/api/insights/* endpoints
        ↓
Built-in UI dashboards / Power BI consumers
```

The UI and API must not query raw operational tables directly for analytics. They must consume the insights repository, which reads from stable `analytics_*` views only.

## Source Switching Requirement

The initial implementation may use live SQL views.

Later, the same view contracts must be replaceable with:

- Materialized views
- Precomputed snapshot tables
- Background aggregation jobs

without changing:

- Frontend components
- API contracts
- Power BI endpoint contracts
- Client-side chart logic

Each analytics view should expose:

- `calculated_at`
- `source_mode`

Example values:

```text
source_mode = live | materialized | snapshot
```

## Scope

### In scope

- Analytics SQL views
- Insights API
- UI dashboard widgets
- Time bucketing by week, month, quarter, year
- Site filtering
- Tenant isolation
- Conflict breakdown
- Request status aggregation
- Resource type filtering: `space`, `person`, optionally `tool`
- Live-query first implementation
- Migration path to snapshots/materialized data

### Out of scope

- User-defined reports
- Drag-and-drop dashboard builder
- Custom formulas
- Custom dimensions
- Report scheduling
- Report subscriptions
- Embedded Power BI
- Cross-tenant analytics
- Data warehouse

## Primary Personas

### Planner / Coordinator

Needs to understand whether spaces and people are generally overloaded, underutilized, or blocked by conflicts.

### Site Admin

Needs operational visibility into one or more sites, but must not see other tenants' data.

### Tenant Admin

Needs high-level health and utilization trends across sites within the tenant.

## Main Dashboard Widgets

### 1. Executive KPI cards

For selected period and optional site:

- Total requests
- Scheduled requests
- Unscheduled requests
- Total conflicts
- Overbooking conflicts
- Criteria mismatch conflicts
- Space utilization percentage
- People utilization percentage

### 2. Utilization trend

Chart total capacity vs. used capacity by time bucket.

Filters:

- Resource type: spaces / people / tools
- Site
- Date range
- Bucket: week / month / quarter / year

### 3. Conflict trend

Chart conflicts by bucket and type.

Conflict types:

- `OVERBOOKING`
- `CRITERIA_MISMATCH`
- `RESOURCE_UNAVAILABLE`
- `SCHEDULE_OUTSIDE_AVAILABILITY`
- `MISSING_RESOURCE`

### 4. Request status summary

Show request totals by status:

- New / Draft
- Unscheduled
- Scheduled
- Completed
- Cancelled

Exact statuses must be mapped to the existing Orkyo domain model.

## Capacity Semantics

### People

Capacity is calculated in minutes or hours based on availability calendars and absences.

```text
total_capacity = available working time in selected period
used_capacity = scheduled assignment duration in selected period
utilization = used_capacity / total_capacity
```

### Spaces

Initial version should use time-based occupancy.

```text
total_capacity = open/available time in selected period
used_capacity = scheduled request occupation time in selected period
utilization = used_capacity / total_capacity
```

Later versions may support area-time capacity, but this must be explicit and must not be mixed with simple time occupancy in the same KPI.

## Non-Functional Requirements

- Tenant isolation is mandatory.
- All APIs must apply tenant context server-side.
- Site filters must be validated against tenant access.
- Queries must be bounded by date range.
- Dashboard defaults must avoid unbounded all-time aggregation.
- API responses must be chart-ready and stable.
- Large tenants must be able to migrate to snapshot-backed views without frontend changes.
