# Orkyo People Utilization Timeline Segments — Specification

## 1. Objective

Refactor the People tab in the Utilization page from a cell-background heatmap into a read-only timeline segment view that visually matches the Spaces scheduler.

The People grid shall show aggregated booking status per person over the selected time range. Consecutive time units with the same aggregated status shall be merged into one visual segment/bar. These bars are not draggable, resizable, or directly editable. Clicking a segment opens a dialog to inspect, add, or remove active request assignments for that person and period.

## 2. Core UX Principle

The Spaces tab is an interactive scheduler.

The People tab is a capacity/status timeline.

Both should share the same visual language:
- horizontal timeline grid
- resource rows
- colored bars/segments
- consistent legend
- same time-scale behavior

But their interaction model differs:

| Area | Spaces tab | People tab |
|---|---|---|
| Purpose | Schedule space allocations | Review and adjust person assignments |
| Bars | Request allocation bars | Aggregated status segments |
| Drag/move | Yes | No |
| Resize | Yes | No |
| Click | Edit/request details | Open person assignment dialog |
| Status source | Request allocation / space conflict | Person availability + assigned request load |

## 3. Status Definitions

The People timeline status shall be derived from a person's availability and request assignments for each timeline unit.

Recommended status model:

```ts
type UtilizationStatus =
  | "available"
  | "partial"
  | "assigned"
  | "overbooked"
  | "off";
```

### Status rules

For each person and timeline unit:

1. If the person is unavailable due to weekend, holiday, absence, or configured non-working time:
   - status = `off`

2. Else calculate utilization:
   - `utilizationPercent = assignedCapacity / availableCapacity * 100`

3. Derive status:
   - `0%` = `available`
   - `> 0% and < 100%` = `partial`
   - `100%` = `assigned`
   - `> 100%` = `overbooked`

### Important

The bar represents the aggregated booking status of the person, not an individual request.

Example for week view:

| Day | Aggregated status |
|---|---|
| Monday | Available |
| Tuesday | Available |
| Wednesday | Overbooked |
| Thursday | Partial |
| Friday | Partial |
| Saturday | Off |
| Sunday | Off |

Rendered as:

```text
[ Available Mon-Tue ][ Overbooked Wed ][ Partial Thu-Fri ][ Off Sat-Sun ]
```

## 4. Segment Generation

The backend or frontend must generate merged utilization segments.

A segment is a contiguous period where the calculated status remains the same for the same person.

```ts
interface PersonUtilizationSegment {
  personId: string;
  personName: string;
  departmentId?: string;
  departmentName?: string;
  jobTitle?: string;

  start: string; // ISO datetime/date depending on scale
  end: string;   // exclusive ISO datetime/date depending on scale

  status: UtilizationStatus;
  utilizationPercent: number;
  requestCount: number;

  hasRequirementMismatch: boolean;
  mismatchCount: number;

  sourceUnitCount: number;
}
```

### Merge rule

Timeline units may be merged when all of the following are equal:
- same person
- adjacent time units
- same `status`

Recommended optional merge constraints:
- same `hasRequirementMismatch`
- same off reason if off reason is exposed visually

Do not merge across non-contiguous units.

## 5. Time Scale Behavior

The People grid must respect the same selected scale as the Utilization page.

Initial support should cover:
- Week
- Day
- Month if currently supported by the scheduler
- Year only if existing scheduler already handles it reliably

The segment granularity shall follow the selected scale:

| Scale | Suggested calculation unit |
|---|---|
| Day | hour or existing scheduler slot |
| Week | day |
| Month | day |
| Year | week or month, depending on existing scheduler model |

Use the existing Utilization date navigation state:
- selected date
- range start/end
- scale
- timezone/site calendar handling

## 6. Visual Design

### Legend

Reuse the existing color legend:
- Available
- Partial
- Assigned
- Overbooked
- Off

For the People tab, the legend must refer to aggregated person capacity status.

### Segment rendering

Each segment appears as a horizontal bar in the person's timeline row.

Requirements:
- aligned to the same time columns as the Space grid
- segment width equals its time duration
- segment position equals its start/end period
- bars are visually distinct from plain cell background
- cursor indicates clickable, not draggable
- no resize handles
- no drag cursor
- no drop target behavior

### Labels

Recommended label inside segment:
- `Available`
- `Partial 65%`
- `Assigned 100%`
- `Overbooked 125%`
- `Off`

For narrow segments, hide label and expose details through tooltip.

### Tooltip

On hover, show:
- Person name
- Period
- Status
- Utilization percent
- Request count
- Mismatch count if any

### Requirement mismatch indication

If one or more assigned/available requests for the clicked period do not match the person's requirements/skills:
- highlight the segment with a warning marker or subtle badge
- show mismatch count in tooltip
- show exact mismatch details in the dialog

Avoid using mismatch highlighting as the main status color. The main color must remain the booking status.

## 7. Interaction Model

### Click segment

Clicking a segment opens the Person Assignment Dialog.

The dialog context is:
- selected person
- segment start
- segment end
- current scale
- current site/tenant context if applicable

### Non-interactions

People segments must not support:
- drag
- resize
- direct in-grid assignment
- drop from request list
- keyboard move/resize operations

## 8. Person Assignment Dialog

The dialog shall allow the user to:
- see current assignments for the selected person and period
- add relevant active requests
- remove assignments
- identify requirement mismatches before assigning
- save changes

### Dialog filter rules

For the selected period, show:

1. Assigned requests:
   - requests assigned to this person
   - overlapping the selected segment period

2. Candidate requests:
   - unscheduled/unassigned people requests that overlap the selected period
   - scheduled requests for the given period where person assignment can be changed/added
   - only active/open statuses according to existing request lifecycle

Do not show requests that do not overlap the selected period.

### Requirement matching

For each candidate request:
- evaluate required criteria/skills against the person's capabilities
- mark as:
  - match
  - partial match
  - mismatch

Do not hard-block assignment unless existing domain rules already require blocking. The default should be warn but allow, because planners may intentionally assign partially matching people.

### Dialog content

Recommended sections:

```text
Person Assignment

Person:
Name, role/job title, department/group

Period:
Start - End

Current status:
Partial / Assigned / Overbooked / Off
Utilization: X%
Active request count: N

Assigned requests:
- request name
- requested period
- capacity contribution
- requirement match state
- remove action

Available/candidate requests:
- request name
- requested period
- required skills/criteria
- match state
- add action

Warnings:
- overbooking warning
- off-time assignment warning
- requirement mismatch warning
```

### Save behavior

On save:
- persist assignment additions/removals using existing request/person assignment APIs where possible
- recalculate person utilization
- update visible segments
- preserve current date/scale/search/filter state

## 9. Data/API Expectations

Prefer reusing existing utilization APIs if they already calculate resource utilization generically.

If not available, introduce a dedicated endpoint:

```http
GET /api/utilization/people?start=...&end=...&scale=...&siteId=...
```

Response:

```json
{
  "range": {
    "start": "2026-06-07",
    "end": "2026-06-14",
    "scale": "week"
  },
  "groups": [
    {
      "id": "production-crew",
      "name": "Production Crew",
      "people": [
        {
          "id": "person-1",
          "name": "Adaline Hoeger",
          "jobTitle": "Production Manager 4",
          "segments": [
            {
              "start": "2026-06-07",
              "end": "2026-06-09",
              "status": "available",
              "utilizationPercent": 0,
              "requestCount": 0,
              "hasRequirementMismatch": false,
              "mismatchCount": 0,
              "sourceUnitCount": 2
            },
            {
              "start": "2026-06-09",
              "end": "2026-06-10",
              "status": "overbooked",
              "utilizationPercent": 125,
              "requestCount": 3,
              "hasRequirementMismatch": true,
              "mismatchCount": 1,
              "sourceUnitCount": 1
            }
          ]
        }
      ]
    }
  ]
}
```

For the dialog, use either existing request APIs or add:

```http
GET /api/people/{personId}/assignment-options?start=...&end=...
```

Response should include:
- assigned requests
- candidate requests
- utilization summary
- requirement match result per request

Mutation APIs may be existing or added:

```http
POST /api/people/{personId}/request-assignments
DELETE /api/people/{personId}/request-assignments/{assignmentId}
```

If existing request assignment APIs already exist, do not duplicate them.

## 10. Acceptance Criteria

### Grid rendering

- People tab renders timeline bars instead of full-cell background heatmap.
- Consecutive periods with same aggregated status are merged into one segment.
- Segments align with the visible timeline columns.
- Segment colors match the legend.
- Segment labels/tooltip expose status and utilization details.
- Weekend/off periods render as off segments.
- Segments are read-only: no drag, no resize, no drop behavior.

### Dialog

- Clicking a segment opens a person assignment dialog.
- Dialog is scoped to the clicked person and segment period.
- Dialog shows assigned requests overlapping the segment period.
- Dialog shows candidate unscheduled or relevant scheduled active requests for the same period.
- Requests outside the segment period are hidden.
- Requirement mismatches are clearly highlighted.
- Adding/removing assignments updates the utilization segments after save.

### Data correctness

- Available, partial, assigned, overbooked, and off statuses are calculated consistently.
- Overbooking is detected when assigned capacity exceeds available capacity.
- Off-time is respected from weekends, holidays, and absences.
- Requirement matching uses the existing criteria/capability model.
- Tenant isolation is preserved for all APIs and queries.

### UX consistency

- People and Spaces tabs share the same time navigation and visual grammar.
- People tab is clearly capacity/status oriented, not a drag-and-drop scheduler.
- Existing search/grouping behavior remains functional.

## 11. Non-Goals

This implementation must not:
- introduce drag-and-drop people scheduling in the People tab
- replace the Spaces scheduler
- redesign the entire Utilization page
- change the resource schema unless strictly required
- introduce a separate reporting engine
- change tenant isolation architecture
