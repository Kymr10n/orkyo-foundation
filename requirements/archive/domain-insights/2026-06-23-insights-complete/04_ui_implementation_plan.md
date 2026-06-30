# 04 — UI Implementation Plan

## Navigation

Add either:

1. A new top-level `Insights` page, or
2. A new `Insights` tab inside `Utilization`.

Recommendation: start as a tab under `Utilization` because the first dashboards are operational utilization insights, not enterprise reporting.

Suggested tabs:

```text
Utilization
- Spaces
- People
- Calendar
- Insights
```

## Page Structure

```text
Insights Header
- Site selector
- Date range selector
- Bucket selector: week / month / quarter / year

KPI card row
- Total requests
- Scheduled
- Unscheduled
- Total conflicts
- Space utilization
- People utilization

Charts
- Space utilization trend
- People utilization trend
- Conflict trend
- Request status trend
```

## UX Principles

- This is not a report builder.
- Keep filters minimal.
- Default to current year or last 12 months.
- Use consistent time buckets across charts.
- KPI cards should link to existing filtered operational views where possible.
- Display `calculatedAt` and `sourceMode` subtly for admin/debug transparency.

## Suggested Components

```text
InsightsPage
InsightsFilters
KpiCard
UtilizationTrendChart
ConflictTrendChart
RequestStatusTrendChart
useInsightsOverview
useResourceUtilizationInsights
useConflictInsights
useRequestInsights
```

## Frontend Data Hooks

Example hooks:

```ts
useInsightsOverview({ siteId, from, to })
useInsightsUtilization({ resourceType, siteId, from, to, bucket })
useInsightsConflicts({ siteId, from, to, bucket })
useInsightsRequests({ siteId, from, to, bucket })
```

## Empty States

Required states:

- No requests in period
- No scheduled requests in period
- No conflicts in period
- No capacity configured
- Selected date range too large
- API unavailable

## Chart Recommendations

### Utilization trend

Line chart:

- Total capacity
- Used capacity

Optional second view:

- Utilization percentage

Avoid mixing minutes/hours and percentages in one chart unless the charting library supports dual axis clearly.

### Conflict trend

Stacked bar chart if available.

If not, use grouped bars or separate lines by conflict type.

### Request status

Stacked bar chart by status, or a compact line/bar trend.

## Drill-Down Behavior

Initial version:

- Clicking total conflicts opens conflict page filtered by period/site.
- Clicking unscheduled requests opens requests list filtered by unscheduled status.
- Clicking scheduled requests opens requests list filtered by scheduled status.
- Clicking utilization charts may later open resource-level details.

## Admin Transparency

Show small footer text:

```text
Calculated at 12 Jun 2026 10:00 · Source: live
```

Later this helps validate migration to snapshot/materialized data.
