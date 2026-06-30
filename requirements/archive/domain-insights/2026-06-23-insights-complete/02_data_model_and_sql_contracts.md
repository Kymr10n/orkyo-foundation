# 02 — Data Model and SQL Contracts

## Design Goal

Create stable analytics database views that can initially use live joins against operational tables, but can later be switched to materialized views or snapshot tables.

The stable contracts are more important than the initial implementation.

## Recommended Views

```sql
analytics_request_summary_v
analytics_request_status_trend_v
analytics_resource_capacity_v
analytics_resource_utilization_v
analytics_conflict_summary_v
analytics_metric_fact_v
```

## Common Columns

All analytics views should include these columns where applicable:

```sql
tenant_id
site_id
bucket_type          -- week | month | quarter | year
bucket_start         -- inclusive
bucket_end           -- exclusive
calculated_at
source_mode          -- live | materialized | snapshot
```

## View: analytics_resource_utilization_v

Purpose: chart and summarize resource utilization for spaces, people, and optionally tools.

```sql
CREATE VIEW analytics_resource_utilization_v AS
SELECT
  tenant_id,
  site_id,
  resource_type,
  resource_id,
  bucket_type,
  bucket_start,
  bucket_end,
  total_capacity_minutes,
  used_capacity_minutes,
  GREATEST(total_capacity_minutes - used_capacity_minutes, 0) AS available_capacity_minutes,
  CASE
    WHEN total_capacity_minutes > 0
    THEN ROUND((used_capacity_minutes::numeric / total_capacity_minutes::numeric) * 100, 2)
    ELSE NULL
  END AS utilization_percent,
  conflict_count,
  NOW() AS calculated_at,
  'live'::text AS source_mode
FROM ...;
```

Required dimensions:

```text
resource_type = space | person | tool
bucket_type = week | month | quarter | year
```

Notes:

- Use minutes internally for precision.
- API may convert to hours for display.
- `bucket_end` must be exclusive.
- `utilization_percent` may exceed 100 if overbooking is allowed and used capacity exceeds capacity. Do not clamp it unless the UI specifically wants capped visualization.

## View: analytics_resource_capacity_v

Purpose: inspect total capacity independent of utilization.

```sql
analytics_resource_capacity_v
- tenant_id
- site_id
- resource_type
- resource_id
- bucket_type
- bucket_start
- bucket_end
- total_capacity_minutes
- unavailable_minutes
- net_capacity_minutes
- calculated_at
- source_mode
```

## View: analytics_conflict_summary_v

Purpose: count conflicts by type, severity, resource type, and time bucket.

```sql
analytics_conflict_summary_v
- tenant_id
- site_id
- bucket_type
- bucket_start
- bucket_end
- conflict_type
- severity
- resource_type
- conflict_count
- affected_request_count
- calculated_at
- source_mode
```

Recommended conflict types:

```text
OVERBOOKING
CRITERIA_MISMATCH
RESOURCE_UNAVAILABLE
SCHEDULE_OUTSIDE_AVAILABILITY
MISSING_RESOURCE
UNKNOWN
```

## View: analytics_request_summary_v

Purpose: provide period-level request counts.

```sql
analytics_request_summary_v
- tenant_id
- site_id
- bucket_type
- bucket_start
- bucket_end
- total_requests
- scheduled_requests
- unscheduled_requests
- completed_requests
- cancelled_requests
- requests_with_conflicts
- calculated_at
- source_mode
```

## View: analytics_request_status_trend_v

Purpose: chart request status over time.

```sql
analytics_request_status_trend_v
- tenant_id
- site_id
- bucket_type
- bucket_start
- bucket_end
- request_status
- request_count
- calculated_at
- source_mode
```

## View: analytics_metric_fact_v

Purpose: generic BI-friendly fact view.

This is useful for Power BI, exports, and future generalized insight consumption.

```sql
analytics_metric_fact_v
- tenant_id
- site_id
- bucket_type
- bucket_start
- bucket_end
- metric_name
- metric_value_numeric
- metric_value_text
- dimension_type
- dimension_id
- dimension_name
- source_entity_type
- calculated_at
- source_mode
```

Example metric names:

```text
resource.total_capacity_minutes
resource.used_capacity_minutes
resource.utilization_percent
conflict.total_count
conflict.overbooking_count
conflict.criteria_mismatch_count
request.total_count
request.scheduled_count
request.unscheduled_count
```

## Bucket Generation

Use a shared bucket generation strategy.

Preferred PostgreSQL approach:

```sql
generate_series(:from, :to, interval '1 month')
```

Bucket definitions:

```text
week    = ISO week, Monday start
month   = calendar month
quarter = calendar quarter
year    = calendar year
```

## Live-to-Snapshot Switch

Initial live view:

```sql
CREATE VIEW analytics_resource_utilization_v AS
SELECT ...
FROM operational_tables
WHERE ...;
```

Future snapshot table:

```sql
CREATE TABLE analytics_resource_utilization_snapshot (
  tenant_id uuid NOT NULL,
  site_id uuid NULL,
  resource_type text NOT NULL,
  resource_id uuid NULL,
  bucket_type text NOT NULL,
  bucket_start timestamptz NOT NULL,
  bucket_end timestamptz NOT NULL,
  total_capacity_minutes integer NOT NULL,
  used_capacity_minutes integer NOT NULL,
  available_capacity_minutes integer NOT NULL,
  utilization_percent numeric(6,2),
  conflict_count integer NOT NULL DEFAULT 0,
  calculated_at timestamptz NOT NULL,
  is_current boolean NOT NULL DEFAULT true,
  PRIMARY KEY (tenant_id, site_id, resource_type, resource_id, bucket_type, bucket_start, calculated_at)
);
```

Future snapshot-backed view:

```sql
CREATE OR REPLACE VIEW analytics_resource_utilization_v AS
SELECT
  tenant_id,
  site_id,
  resource_type,
  resource_id,
  bucket_type,
  bucket_start,
  bucket_end,
  total_capacity_minutes,
  used_capacity_minutes,
  available_capacity_minutes,
  utilization_percent,
  conflict_count,
  calculated_at,
  'snapshot'::text AS source_mode
FROM analytics_resource_utilization_snapshot
WHERE is_current = true;
```

## Indexing for Future Snapshot Tables

When snapshot tables are introduced:

```sql
CREATE INDEX idx_analytics_util_snapshot_tenant_bucket
ON analytics_resource_utilization_snapshot (tenant_id, bucket_type, bucket_start, bucket_end);

CREATE INDEX idx_analytics_util_snapshot_tenant_site
ON analytics_resource_utilization_snapshot (tenant_id, site_id, bucket_type, bucket_start);

CREATE INDEX idx_analytics_conflict_snapshot_tenant_bucket
ON analytics_conflict_summary_snapshot (tenant_id, bucket_type, bucket_start, conflict_type);
```

## Important Implementation Rule

The API must never depend on whether the analytics views are backed by live queries, materialized views, or snapshots.
