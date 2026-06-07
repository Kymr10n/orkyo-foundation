# Orkyo People Utilization Timeline Segments — Implementation Plan

## Phase 0 — Baseline Review

1. Locate the Utilization page implementation.
2. Identify existing components for:
   - Spaces scheduler grid
   - People utilization grid
   - timeline scale/date navigation
   - request bars
   - legends
   - utilization calculation
   - request/person assignment APIs
3. Confirm whether utilization is calculated backend-side, frontend-side, or mixed.
4. Confirm how people availability is currently modeled:
   - weekends
   - holidays
   - absences
   - partial availability
   - request assignment capacity

Deliverable:
- short implementation note in the PR describing the discovered current architecture.

## Phase 1 — Introduce Shared Status/Segment Model

1. Add or reuse a shared type for utilization status:

```ts
type UtilizationStatus =
  | "available"
  | "partial"
  | "assigned"
  | "overbooked"
  | "off";
```

2. Add a reusable `PersonUtilizationSegment` type.
3. Add helper functions:
   - calculate person utilization per time unit
   - derive status from utilization/off-time
   - merge adjacent units into segments

Suggested helper functions:

```ts
deriveUtilizationStatus(input): UtilizationStatus
mergeUtilizationUnitsToSegments(units): PersonUtilizationSegment[]
formatSegmentLabel(segment): string
```

4. Add unit tests for status derivation and segment merging.

Test examples:
- Mon/Tue available merge into one segment
- Wed overbooked remains separate
- Thu/Fri partial merge into one segment
- Sat/Sun off merge into one segment
- partial must not merge with assigned
- off must not merge with available
- adjacent same status merges correctly
- non-adjacent same status does not merge

## Phase 2 — Backend/API Work

Prefer backend-side segment generation if existing utilization data is already calculated server-side.

1. Add or adapt people utilization query.
2. Ensure query is tenant-scoped.
3. Ensure site/calendar context is respected.
4. Return grouped people with segments.

Recommended endpoint:

```http
GET /api/utilization/people?start=...&end=...&scale=...&siteId=...
```

5. Include:
   - person metadata
   - group/department metadata
   - segment start/end
   - status
   - utilization percent
   - request count
   - mismatch flags/counts

6. Implement or reuse dialog data endpoint:

```http
GET /api/people/{personId}/assignment-options?start=...&end=...
```

7. Endpoint must return:
   - assigned overlapping requests
   - candidate active requests overlapping the period
   - requirement match result
   - current utilization summary

8. Reuse existing assignment mutation endpoints if available. Only add new ones if required.

Security requirements:
- all queries tenant-scoped
- never return requests from another tenant
- validate person and request tenant ownership before mutation
- enforce existing authorization rules

## Phase 3 — People Timeline UI Component

1. Replace the current People grid heatmap rendering with timeline segment rendering.
2. Keep existing:
   - group rows
   - person rows
   - search
   - selected scale/date range
   - legend
3. Render one row per person.
4. Render segments positioned by start/end against the current timeline.
5. Use same grid column calculations as the Spaces scheduler where possible.
6. Disable drag/drop/resize behavior for People segments.
7. Add cursor and accessibility behavior:
   - clickable segment
   - keyboard focusable
   - Enter/Space opens dialog
   - aria-label contains person, period, status, utilization

Visual requirements:
- use same color tokens as Spaces/utilization legend
- warning badge/marker for requirement mismatch
- label hidden gracefully when segment too narrow
- tooltip for full details

## Phase 4 — Person Assignment Dialog

1. Create or adapt dialog component:

```text
PersonAssignmentDialog
```

Props:
```ts
personId
personName
start
end
scale
onClose
onSaved
```

2. On open, load assignment options.
3. Render:
   - person header
   - period
   - utilization status
   - assigned requests
   - candidate requests
   - mismatch warnings
4. Add/remove assignments locally in dialog state.
5. On save, persist changes.
6. Refresh People utilization segments after successful save.

Requirement matching display:
- match = neutral/success indicator
- partial/mismatch = warning indicator
- show missing criteria/skills in expandable detail or tooltip

Important:
- Do not show requests outside selected segment period.
- Do not block mismatch assignment unless existing business rules require it.
- Warn clearly before saving mismatching or overbooking assignments.

## Phase 5 — Integration With Existing State

1. Ensure People tab responds to:
   - date navigation
   - scale change
   - search
   - group collapse/expand
   - tenant/site context
2. Ensure segment data refetches when relevant state changes.
3. Preserve current state after dialog save.
4. Add loading/empty/error states:
   - no people
   - no matching people
   - failed to load utilization
   - failed to save assignment changes

## Phase 6 — Tests

### Unit tests

Add tests for:
- utilization status derivation
- segment merging
- off-time precedence
- overbooking detection
- requirement mismatch flag aggregation
- request period overlap filtering

### Component tests

Add tests for:
- rendering merged segments
- clicking segment opens dialog with correct period
- narrow labels hidden or tooltip available
- no drag/resize handles rendered
- search still filters people
- grouped rows still work

### API tests

Add tests for:
- tenant isolation
- period overlap filtering
- assigned vs candidate request classification
- requirement match results
- assignment mutation validation

## Phase 7 — Manual QA Scenarios

Use or seed data covering:

1. Person available all week.
   - One green segment for weekdays, off segment for weekend if applicable.

2. Person partial on Thu/Fri.
   - One orange segment Thu-Fri.

3. Person overbooked on Wed.
   - One red segment Wed.

4. Person off on weekend.
   - One grey segment Sat-Sun.

5. Mixed week:
   - Available Mon-Tue
   - Overbooked Wed
   - Partial Thu-Fri
   - Off Sat-Sun

6. Click each segment.
   - Dialog period equals clicked segment range.
   - Assigned/candidate requests are period-relevant only.

7. Requirement mismatch.
   - Segment shows mismatch marker.
   - Dialog shows missing requirements.

8. Save assignment change.
   - Dialog closes or confirms.
   - Grid refreshes.
   - Status changes correctly.

## Phase 8 — Rollout Considerations

Recommended rollout:
1. Keep behind feature flag if feature flags exist.
2. Validate against existing demo data.
3. Compare old heatmap statuses against new segment statuses for at least one seeded dataset.
4. Remove old heatmap code after validation.

Suggested feature flag:
```text
people_utilization_timeline_segments
```

## Implementation Notes for Claude/Copilot

- Prefer minimal schema changes.
- Reuse existing resource/person/request assignment model.
- Reuse existing color tokens and scheduler timeline math.
- Do not introduce drag-and-drop behavior for People segments.
- Keep the dialog scoped and simple.
- Preserve tenant isolation and authorization as first-class acceptance criteria.
