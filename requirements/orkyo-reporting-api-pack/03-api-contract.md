# 03 — API Contract Draft

Base path:

```text
/api/reporting/v1
```

Authentication:

```http
Authorization: Bearer orkyo_rpt_<prefix>_<secret>
```

Tenant is resolved from token. No endpoint accepts `tenantId`.

## Common Response Envelope

```json
{
  "data": [],
  "page": {
    "page": 1,
    "pageSize": 500,
    "totalItems": 1234,
    "hasNextPage": true
  },
  "metadata": {
    "from": "2026-01-01T00:00:00Z",
    "to": "2026-01-31T23:59:59Z",
    "generatedAtUtc": "2026-05-27T12:00:00Z"
  }
}
```

## Common Query Parameters

```text
from             optional ISO 8601 timestamp/date
to               optional ISO 8601 timestamp/date
updatedSince     optional ISO 8601 timestamp/date
page             optional integer, default 1
pageSize         optional integer, default 500, max 5000
format           optional json|csv
sort             optional controlled sort key
```

Reject unsupported filters explicitly. Do not silently ignore unknown security-sensitive parameters.

## Endpoint: Space Utilization

```http
GET /api/reporting/v1/spaces/utilization?from=2026-01-01&to=2026-01-31
```

DTO:

```json
{
  "siteName": "Main Site",
  "spaceName": "Assembly Hall A",
  "spaceGroupName": "Assembly",
  "periodStartUtc": "2026-01-01T00:00:00Z",
  "periodEndUtc": "2026-01-31T23:59:59Z",
  "availableHours": 160.0,
  "allocatedHours": 112.5,
  "utilizationPercent": 70.3,
  "overbookedHours": 0.0,
  "requestCount": 14
}
```

## Endpoint: Resource Utilization

```http
GET /api/reporting/v1/resources/utilization?from=2026-01-01&to=2026-01-31
```

DTO:

```json
{
  "resourceType": "Person",
  "resourceName": "Jane Doe",
  "resourceGroupName": "Assembly Team",
  "periodStartUtc": "2026-01-01T00:00:00Z",
  "periodEndUtc": "2026-01-31T23:59:59Z",
  "availableHours": 144.0,
  "allocatedHours": 120.0,
  "utilizationPercent": 83.3,
  "absenceHours": 16.0,
  "overbookedHours": 4.0
}
```

Privacy note: expose people names only if tenant admin enables people-level reporting. Otherwise aggregate by group or role.

## Endpoint: Allocations

```http
GET /api/reporting/v1/allocations?from=2026-01-01&to=2026-01-31
```

DTO:

```json
{
  "allocationId": "rpt_alloc_123",
  "requestReference": "REQ-2026-0042",
  "requestTitle": "Prototype Build",
  "resourceType": "Space",
  "resourceName": "Assembly Hall A",
  "siteName": "Main Site",
  "startsAtUtc": "2026-01-12T08:00:00Z",
  "endsAtUtc": "2026-01-12T16:00:00Z",
  "durationHours": 8.0,
  "status": "Scheduled",
  "updatedAtUtc": "2026-01-10T10:15:00Z"
}
```

## Endpoint: Request Throughput

```http
GET /api/reporting/v1/requests/throughput?from=2026-01-01&to=2026-01-31
```

DTO:

```json
{
  "periodStartUtc": "2026-01-01T00:00:00Z",
  "periodEndUtc": "2026-01-31T23:59:59Z",
  "createdCount": 22,
  "scheduledCount": 18,
  "completedCount": 14,
  "cancelledCount": 2,
  "averageLeadTimeHours": 36.5,
  "averageSchedulingDelayHours": 8.2
}
```

## Endpoint: Absences

```http
GET /api/reporting/v1/absences?from=2026-01-01&to=2026-01-31
```

DTO:

```json
{
  "resourceType": "Person",
  "resourceGroupName": "Assembly Team",
  "absenceCategory": "Vacation",
  "startsAtUtc": "2026-01-15T00:00:00Z",
  "endsAtUtc": "2026-01-17T23:59:59Z",
  "absenceHours": 24.0
}
```

Default should aggregate or pseudonymize people-level absence details unless explicitly enabled.

## Endpoint: Conflicts

```http
GET /api/reporting/v1/conflicts?from=2026-01-01&to=2026-01-31
```

DTO:

```json
{
  "conflictType": "Overbooking",
  "resourceType": "Space",
  "resourceName": "Assembly Hall A",
  "requestReference": "REQ-2026-0042",
  "startsAtUtc": "2026-01-12T10:00:00Z",
  "endsAtUtc": "2026-01-12T12:00:00Z",
  "severity": "High",
  "overbookedHours": 2.0
}
```

## Endpoint: Capacity vs Demand

```http
GET /api/reporting/v1/capacity-vs-demand?from=2026-01-01&to=2026-01-31&granularity=week
```

DTO:

```json
{
  "resourceType": "Space",
  "resourceGroupName": "Assembly",
  "periodStartUtc": "2026-01-05T00:00:00Z",
  "periodEndUtc": "2026-01-11T23:59:59Z",
  "availableHours": 320.0,
  "demandHours": 410.0,
  "allocatedHours": 300.0,
  "unallocatedDemandHours": 110.0,
  "capacityGapHours": 90.0
}
```

## Error Responses

Unauthorized:

```json
{
  "error": "unauthorized",
  "message": "Invalid reporting token."
}
```

Forbidden:

```json
{
  "error": "forbidden",
  "message": "The reporting token does not have access to this dataset."
}
```

Validation:

```json
{
  "error": "validation_failed",
  "message": "Invalid query parameters.",
  "details": [
    {
      "field": "pageSize",
      "message": "Maximum pageSize is 5000."
    }
  ]
}
```
