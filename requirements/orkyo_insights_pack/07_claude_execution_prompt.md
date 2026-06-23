# 07 — Claude/Copilot Execution Prompt

Use this prompt to instruct Claude Code or Copilot.

---

We need to implement a lightweight built-in Insights capability for Orkyo without building a general-purpose reporting engine.

Key architectural rule:

The UI and API must consume stable analytics views via an Insights repository/service. They must not query raw operational tables directly for dashboard data.

Goal:

Create an analytics semantic layer that initially uses live SQL views, but can later be switched to materialized views or snapshot tables without changing frontend or API contracts.

Implement or prepare the following:

1. Inspect the existing codebase and identify:
   - request model/table
   - scheduling fields
   - resource assignment model
   - spaces/people/tools model
   - availability/offtime model
   - conflict validation implementation
   - tenant isolation pattern
   - existing utilization page structure
   - existing API route conventions
   - existing charting/UI component conventions

2. Add database analytics views with stable contracts:
   - `analytics_request_summary_v`
   - `analytics_request_status_trend_v`
   - `analytics_resource_capacity_v`
   - `analytics_resource_utilization_v`
   - `analytics_conflict_summary_v`
   - `analytics_metric_fact_v`

3. These views must include, where applicable:
   - `tenant_id`
   - `site_id`
   - `bucket_type`
   - `bucket_start`
   - `bucket_end`
   - `calculated_at`
   - `source_mode`

4. Initial implementation may use live queries. Use `source_mode = 'live'`.

5. Backend must expose an Insights repository/service with methods similar to:
   - `getOverview(filter)`
   - `getUtilization(filter)`
   - `getConflictTrend(filter)`
   - `getRequestTrend(filter)`

6. Repository must read from analytics views only.

7. Add API endpoints:
   - `GET /api/insights/overview`
   - `GET /api/insights/utilization`
   - `GET /api/insights/conflicts`
   - `GET /api/insights/requests`

8. Query parameters:
   - `siteId` optional
   - `from` required
   - `to` required
   - `bucket` required for trend endpoints: `week | month | quarter | year`
   - `resourceType` required for utilization: `space | person | tool`

9. Enforce tenant isolation server-side. Do not trust client-provided tenant IDs.

10. Validate date ranges and prevent unbounded analytics queries.

11. Add frontend hooks:
   - `useInsightsOverview`
   - `useInsightsUtilization`
   - `useInsightsConflicts`
   - `useInsightsRequests`

12. Add an `Insights` tab under `Utilization` unless the existing navigation structure strongly suggests a top-level page.

13. UI should include:
   - site selector
   - date range selector
   - bucket selector
   - KPI cards
   - space utilization trend
   - people utilization trend
   - conflict trend
   - request status trend

14. Do not build:
   - custom report builder
   - user-defined metrics
   - report scheduling
   - embedded BI
   - custom dashboard designer

15. Add tests for:
   - tenant isolation
   - site filtering
   - date range validation
   - bucket validation
   - empty states
   - request counts
   - scheduled vs unscheduled counts
   - conflict aggregation
   - utilization calculation

16. Important: do not fake analytics values. If a value cannot yet be calculated correctly from current model, return `NULL` or document a deliberate limitation.

17. Preserve a clean migration path so that later the analytics views can be replaced with snapshot-backed views without changing API or frontend code.

---
