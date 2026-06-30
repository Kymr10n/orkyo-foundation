# 03 — Insights API Contract

## General Rules

All endpoints must be tenant-scoped server-side.

The client must not provide `tenantId` unless the existing Orkyo architecture already safely supports tenant selection for tenant admins. The authenticated tenant context should be authoritative.

Common query parameters:

```text
siteId      optional
from        required, ISO date or datetime
to          required, ISO date or datetime, exclusive where practical
bucket      week | month | quarter | year
resourceType space | person | tool
```

Date ranges must be bounded.

Recommended maximums:

```text
week bucket:    max 2 years
month bucket:   max 5 years
quarter bucket: max 10 years
year bucket:    max 20 years
```

## GET /api/insights/overview

Purpose: return KPI cards for selected period.

Example:

```http
GET /api/insights/overview?siteId=...&from=2026-01-01&to=2026-12-31
```

Response:

```json
{
  "period": {
    "from": "2026-01-01T00:00:00Z",
    "to": "2027-01-01T00:00:00Z"
  },
  "siteId": "...",
  "requests": {
    "total": 1240,
    "scheduled": 980,
    "unscheduled": 210,
    "completed": 730,
    "cancelled": 50
  },
  "conflicts": {
    "total": 87,
    "overbooking": 32,
    "criteriaMismatch": 41,
    "resourceUnavailable": 14
  },
  "utilization": {
    "spacesPercent": 74.2,
    "peoplePercent": 81.7,
    "toolsPercent": null
  },
  "metadata": {
    "calculatedAt": "2026-06-12T10:00:00Z",
    "sourceMode": "live"
  }
}
```

## GET /api/insights/utilization

Purpose: return capacity and utilization trend.

Example:

```http
GET /api/insights/utilization?resourceType=space&bucket=month&siteId=...&from=2026-01-01&to=2026-12-31
```

Response:

```json
{
  "resourceType": "space",
  "bucket": "month",
  "series": [
    {
      "bucketStart": "2026-01-01T00:00:00Z",
      "bucketEnd": "2026-02-01T00:00:00Z",
      "totalCapacityMinutes": 72000,
      "usedCapacityMinutes": 53280,
      "availableCapacityMinutes": 18720,
      "utilizationPercent": 74.0,
      "conflictCount": 6
    }
  ],
  "metadata": {
    "calculatedAt": "2026-06-12T10:00:00Z",
    "sourceMode": "live"
  }
}
```

Optional future parameter:

```text
groupBy=site | resource | resourceType
```

Do not implement until needed.

## GET /api/insights/conflicts

Purpose: return conflict trends by type.

Example:

```http
GET /api/insights/conflicts?bucket=month&siteId=...&from=2026-01-01&to=2026-12-31
```

Response:

```json
{
  "bucket": "month",
  "series": [
    {
      "bucketStart": "2026-01-01T00:00:00Z",
      "bucketEnd": "2026-02-01T00:00:00Z",
      "total": 14,
      "overbooking": 5,
      "criteriaMismatch": 7,
      "resourceUnavailable": 2,
      "scheduleOutsideAvailability": 0,
      "missingResource": 0
    }
  ],
  "metadata": {
    "calculatedAt": "2026-06-12T10:00:00Z",
    "sourceMode": "live"
  }
}
```

## GET /api/insights/requests

Purpose: return request status trend and totals.

Example:

```http
GET /api/insights/requests?bucket=month&siteId=...&from=2026-01-01&to=2026-12-31
```

Response:

```json
{
  "bucket": "month",
  "series": [
    {
      "bucketStart": "2026-01-01T00:00:00Z",
      "bucketEnd": "2026-02-01T00:00:00Z",
      "total": 105,
      "scheduled": 82,
      "unscheduled": 18,
      "completed": 4,
      "cancelled": 1
    }
  ],
  "metadata": {
    "calculatedAt": "2026-06-12T10:00:00Z",
    "sourceMode": "live"
  }
}
```

## Error Handling

Use existing Orkyo API error conventions.

Required cases:

```text
400 invalid bucket
400 invalid date range
400 date range too large
403 site not accessible
404 site not found, if appropriate
500 analytics calculation failed
```

## Repository Interface

Backend should expose a repository/service boundary similar to:

```text
InsightsRepository
- getOverview(ctx, filter)
- getUtilization(ctx, filter)
- getConflictTrend(ctx, filter)
- getRequestTrend(ctx, filter)
```

Where `ctx` contains tenant and authorization context.

Implementation must read from `analytics_*_v` views only.
