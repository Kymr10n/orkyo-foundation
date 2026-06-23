# 05 — Implementation Plan

## Phase 0 — Discovery

Before changing implementation, inspect existing codebase for:

- Request table/model
- Schedule fields
- Resource assignment tables
- Space/person/tool resource model
- Availability/offtime model
- Conflict validation implementation
- Tenant isolation patterns
- Existing utilization page API calls
- Existing charting components/libraries

Deliverable:

- Confirm exact source tables and status enum names.
- Confirm whether conflicts are persisted or calculated live.
- Confirm current authorization/tenant middleware pattern.

## Phase 1 — Analytics View Contracts

Create migration for analytics views:

```text
analytics_request_summary_v
analytics_request_status_trend_v
analytics_resource_capacity_v
analytics_resource_utilization_v
analytics_conflict_summary_v
analytics_metric_fact_v
```

If exact capacity calculation is not yet available, implement minimum viable view with known fields and TODO-safe placeholders only where unavoidable.

Do not expose misleading values. Prefer `NULL` over fake computed data.

## Phase 2 — Backend Insights Repository

Create repository/service module:

```text
InsightsRepository
InsightsService
InsightsFilter DTO
```

Rules:

- Read only from analytics views.
- Enforce tenant context server-side.
- Validate site belongs to tenant.
- Validate date range.
- Validate bucket value.
- Normalize response shapes.

## Phase 3 — API Endpoints

Implement:

```http
GET /api/insights/overview
GET /api/insights/utilization
GET /api/insights/conflicts
GET /api/insights/requests
```

Add OpenAPI/schema documentation if project already uses it.

## Phase 4 — Frontend Hooks

Implement hooks:

```text
useInsightsOverview
useInsightsUtilization
useInsightsConflicts
useInsightsRequests
```

Use existing API client conventions.

## Phase 5 — Insights UI

Add `Insights` tab to `Utilization`.

Implement:

- Filter bar
- KPI cards
- Space utilization trend
- People utilization trend
- Conflict trend
- Request status trend

Use existing chart library/components where possible.

## Phase 6 — Tests

Backend tests:

- Tenant isolation
- Site access filtering
- Bucket validation
- Date range validation
- Empty result behavior
- Scheduled vs unscheduled counts
- Conflict type aggregation
- Resource utilization calculation

Frontend tests:

- Rendering with populated data
- Empty states
- Filter changes trigger reload
- API error states

## Phase 7 — Future Snapshot Migration

Prepare but do not implement unless needed:

- Snapshot tables
- Aggregation job
- Refresh strategy
- View replacement to snapshot-backed source

The API and UI must remain unchanged.

## Suggested Delivery Order for Claude/Copilot

1. Inspect existing models and routes.
2. Create analytics SQL views using live data.
3. Add backend repository and API.
4. Add tests for API and tenant isolation.
5. Add frontend hooks.
6. Add UI tab and charts.
7. Add documentation and developer notes.
