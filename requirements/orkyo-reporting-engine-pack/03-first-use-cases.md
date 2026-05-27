# First Reporting Use Cases

## Use case 1 — Space utilization overview

### Goal

Allow a site/resource planner to understand how heavily spaces are used over time.

### Users

- Planner
- Site manager
- Tenant admin

### Questions answered

- Which spaces are heavily used?
- Which spaces are underused?
- Which sites have spare capacity?
- How does utilization change by week or month?

### Data source

`reporting.rpt_space_utilization`

### Suggested metrics

- scheduled hours
- available hours
- utilization percentage
- number of allocations
- number of unique requests
- utilization by site
- utilization by space group

### UI placement

Reports → Utilization → Space Utilization

## Use case 2 — Request pipeline

### Goal

Give users visibility into demand and request status.

### Questions answered

- How many requests are planned, scheduled, completed, or cancelled?
- How much requested duration exists per period?
- Which request types create the most demand?
- Where is the backlog increasing?

### Data source

`reporting.rpt_request_pipeline`

### Suggested metrics

- request count by status
- requested hours
- scheduled hours
- backlog count
- average request lead time
- request count by site/resource type

### UI placement

Reports → Requests → Request Pipeline

## Use case 3 — Allocation conflicts and overbooking

### Goal

Identify planning quality issues and operational risk.

### Questions answered

- Which spaces/resources are overbooked?
- Which periods have the most conflicts?
- Which requests are affected by conflicts?
- Are conflicts increasing or decreasing?

### Data source

`reporting.rpt_allocation_conflicts`

### Suggested metrics

- conflict count
- affected requests
- affected resources
- conflict duration
- conflicts by site
- conflicts by resource type

### UI placement

Reports → Planning Quality → Conflicts & Overbooking

## Use case 4 — Resource availability and capacity

### Goal

Show planned capacity for people, spaces, and tools.

### Questions answered

- What capacity is available per resource type?
- Where are capacity bottlenecks?
- How much capacity is reduced by off-times or absences?
- Which groups are most constrained?

### Data source

`reporting.rpt_resource_availability`

### Suggested metrics

- available hours
- unavailable hours
- capacity percentage
- absence hours
- off-time hours
- capacity by group
- capacity by resource type

### UI placement

Reports → Capacity → Resource Availability

## Use case 5 — Absence impact

### Goal

Understand how vacations, sickness, public holidays, maintenance, and other off-times reduce planning capacity.

### Questions answered

- Which absence types reduce capacity most?
- Which sites or groups are affected?
- Which periods have significant capacity reductions?

### Data source

`reporting.rpt_absence_impact`

### Suggested metrics

- absence hours by type
- affected resources
- affected sites
- capacity impact percentage

### UI placement

Reports → Capacity → Absence Impact

## Initial report catalogue

```json
[
  {
    "key": "space-utilization",
    "title": "Space Utilization",
    "category": "Utilization",
    "description": "Understand scheduled usage versus available space capacity.",
    "requiredPermissions": ["reports.view"]
  },
  {
    "key": "request-pipeline",
    "title": "Request Pipeline",
    "category": "Requests",
    "description": "Track demand, backlog, request status, and scheduled throughput.",
    "requiredPermissions": ["reports.view"]
  },
  {
    "key": "allocation-conflicts",
    "title": "Conflicts & Overbooking",
    "category": "Planning Quality",
    "description": "Identify scheduling conflicts and overbooked resources.",
    "requiredPermissions": ["reports.view"]
  },
  {
    "key": "resource-availability",
    "title": "Resource Availability",
    "category": "Capacity",
    "description": "View available capacity across people, spaces, and tools.",
    "requiredPermissions": ["reports.view"]
  },
  {
    "key": "absence-impact",
    "title": "Absence Impact",
    "category": "Capacity",
    "description": "Analyze capacity loss caused by absences, holidays, and off-times.",
    "requiredPermissions": ["reports.view"]
  }
]
```

## MVP recommendation

Start with these three only:

1. Space Utilization
2. Request Pipeline
3. Conflicts & Overbooking

Add resource availability and absence impact after the people/tools resource model is stable.
