# Calendar View for Requests

> **Status:** Spec (reviewed against the live codebase, 2026-06).
> This revision re-anchors the original draft to the components, hooks, and
> endpoints that already exist so the implementation stays **DRY**, **KISS**, and
> consistent with the current Utilization UX. Sections that change existing
> behavior are called out explicitly; everything else reuses what is already shipped.

## Objective

Add a **Calendar** tab to the Utilization section that gives an Outlook-style,
time-oriented view of requests. The calendar is a **time-based projection of
existing Request entities** — not a new scheduling engine and not a replacement for
Space Utilization or People Utilization.

Users must be able to:

- View scheduled requests in Day, Week, and Month calendar views.
- Create new requests directly from the calendar.
- Schedule existing unscheduled requests from the calendar.
- Move scheduled requests (drag).
- Resize scheduled requests.
- Open request details from a calendar entry.
- See conflict state using the colors already used in the rest of the app.

The calendar **must not alter resource assignments** (Spaces / People / Tools) when
moving or resizing. As shown below, this is guaranteed *by construction* — the same
mechanism the existing Space grid already uses — not by a bespoke guard.

---

## Placement (read first)

This feature lives in **orkyo-foundation** and ships to both products through the
shared `TenantApp` route tree. The behavior has a single-tenant analogue
(Community shows a calendar of its own requests), so per the repo placement rule it
belongs in foundation, **not** in SaaS.

- **No runtime wiring here.** No `Program.cs`, no DI registration, no middleware.
  The calendar is a view + hooks on top of existing services; products do the wiring
  they already do for Utilization.
- **No new public API surface.** This spec adds zero backend endpoints and zero
  color tokens (see below). That keeps the foundation package backward-compatible.

---

## Navigation

The Utilization page (`frontend/src/pages/UtilizationPage.tsx`) already renders a
`PageTabs` with two tabs and persists the active tab in the URL (`?tab=`). Add a
third tab:

```text
Utilization
├── Spaces      (tab=space)   ← existing
├── People      (tab=people)  ← existing
└── Calendar    (tab=calendar) ← new
```

Implementation notes (reuse, don't reinvent):

- Extend the `tabs: PageTab[]` array and the `activeTab` union
  (`'space' | 'people'` → `'space' | 'people' | 'calendar'`) in `UtilizationPage.tsx`.
- The calendar uses FullCalendar's own Outlook-style toolbar (prev/next/today +
  Day/Week/Month) rather than the shared `ScaleSelect`/`TimeNavigator` — those expose
  `year`/`hour` scales that have no calendar analogue, and a native toolbar is more
  familiar. The shared header controls are hidden on the Calendar tab. To keep the
  shared data window aligned, FullCalendar's `datesSet` writes the current view's
  scale (`day`/`week`/`month`) and start date back into the store (`scale`/`anchorTs`),
  which is what `useScheduledRequests` and `useConflicts` read.
- Gate create/move/resize affordances on the existing
  `userCanEdit = membership?.role === "admin" || "editor"` check. Viewers get a
  read-only calendar, matching the rest of the page.

---

## Calendar engine

Do **not** hand-build a calendar grid. Use **FullCalendar React** with:

- `@fullcalendar/react`
- `@fullcalendar/daygrid` (Month)
- `@fullcalendar/timegrid` (Day / Week)
- `@fullcalendar/interaction` (select, drag, resize)

Add these as dependencies in `frontend/package.json`.

### Required: theming layer (UX consistency)

The app uses **shadcn/ui + Tailwind** with first-class dark mode. FullCalendar ships
its own DOM/CSS and will **not** match out of the box. A theming layer is a hard
requirement, not a nice-to-have:

- Wrap FullCalendar in a single foundation component (e.g.
  `frontend/src/components/utilization/RequestCalendar.tsx`) that owns all styling.
- Map FullCalendar CSS variables / overrides onto the existing Tailwind design tokens
  (`--background`, `--foreground`, `--border`, `--muted`, `--primary`, etc.) so the
  calendar inherits light/dark automatically — no hardcoded hex.
- Match the look of `SchedulerGrid`/`PeopleUtilizationGrid`: same border radius
  (`rounded-xl border bg-background`), same muted gridlines, same typography scale.

---

## Data model & loading

A calendar event is a **projection of a `RequestInfo`** — no new table, no new DTO,
no `conflictState` field (none exists). Map the real fields:

```typescript
// RequestInfo → FullCalendar EventInput
{
  id: request.id,
  title: request.name,            // NOT request.title — the field is `name`
  start: request.startTs,         // StartTs (nullable; only scheduled items are shown)
  end: request.endTs,             // EndTs
  // color is derived, not stored — see "Colors" below
  extendedProps: {
    status: request.status,       // planned | in_progress | done | cancelled
    request,                      // keep the full object for handlers/dialogs
  },
}
```

### Loading (reuse existing data layer)

- **Source:** the existing site + time-window endpoint
  `GET /api/sites/{siteId}/requests?from=&to=` (`RequestEndpoints.cs:172`,
  `IRequestService.GetScheduledBySiteWindowAsync`). **Do not add a
  `GET /api/requests/calendar` endpoint** — it would duplicate this.
- **Hook:** `useScheduledRequests(siteId, from, to)` (`hooks/useUtilization.ts`).
- **Window:** reuse `getFetchWindow(scale, anchorTs)`
  (`components/utilization/time-grid-utils.ts`) so the calendar and the Space grid
  request the same buffered window and share the React Query cache
  (`["requests","scheduled", siteId, from, to]`).
- **Range change triggers reload** automatically because the query key includes
  `from`/`to`; changing the FullCalendar view updates `scale`/`anchorTs`, which
  recomputes the window. No manual refetch wiring needed.

### Display rules

- **Scheduled requests** (`StartTs && EndTs` set, with a Space assignment →
  `request.isScheduled`) intersecting the visible range are shown.
- **Unscheduled requests are not rendered on the calendar.** They appear only inside
  the "Schedule existing request" dialog (sourced from the backlog,
  `useBacklogRequests()` / `GET /api/requests?scheduled=false`).

---

## Colors

Reuse the **two color systems that already exist** — do not introduce new tokens or
the draft's 5-tier scheme (it doesn't exist in the codebase).

1. **Base = request status**, via `getStatusColor(status)` (`lib/utils/utils.ts`):
   - `planned` → blue, `in_progress` → yellow/amber, `done` → green,
     `cancelled` → muted/strikethrough.
   This keeps the calendar consistent with the status badges users already read.

2. **Overlay = conflict severity**, via `useConflicts()` (`hooks/useConflicts.ts`),
   which is already scoped to the active site/window and returns
   `{ conflictingRequestIds, capabilityConflicts }`:
   - request id in the **error** set → red border/ring on the event,
   - request id in the **warning** set → amber border/ring,
   - otherwise no overlay.
   Severity comes from the existing backend `IConflictService` /
   `RequestConflictInfo` (`warning` / `error`); the frontend does not invent it.

Provide a small legend that reuses the same classes (mirror the existing utilization
legend that maps onto `schedule-colors.ts`). **No new color constants.**

---

## Creating & scheduling

### Empty-slot select

FullCalendar `select` (a date in Month, a time range in Day/Week) opens a small
**Schedule Request** chooser with two actions:

- **Schedule existing request** — list unscheduled requests from
  `useBacklogRequests()`. On confirm, schedule via `useScheduleRequest()` (see body
  below) with `startTs = selection.start`, `endTs = selection.end`.
- **Create new request** — open the existing `RequestFormDialog`
  (`components/requests/RequestFormDialog.tsx`) prefilled with `startTs`/`endTs` from
  the selection, then persist with the existing `createRequest` + `buildCreatePayload`
  path (the create-child flow in `UtilizationPage.tsx` is the template). Do not build
  a new form.

> A request becomes "scheduled" only when it has a Space assignment
> (`request.isScheduled`). Scheduling from an empty slot therefore assigns a space —
> the chooser must let the user pick one (or default per existing scheduling rules).
> This is the one case where an assignment is intentionally *created*; move/resize
> below never change it.

---

## Moving & resizing (no assignment changes)

Both use the **existing** `PATCH /api/requests/{id}/schedule` endpoint
(`RequestEndpoints.cs:92`) via the **existing** `useScheduleRequest()` mutation. The
request body is `ScheduleRequestRequest`:

```json
{ "resourceId": "<existing space id>", "startTs": "...", "endTs": "..." }
```

The integrity guarantee is structural: re-send the request's **current** space
`resourceId` (read with `getSpaceResourceId(request)` from
`domain/scheduling/request-assignments.ts`) together with the new times. This is
exactly what `handleResizeRequest` in `UtilizationPage.tsx:404` already does — so
Spaces / People / Tools assignments cannot change.

- **`eventDrop`** (move): preserve duration (`endTs - startTs`), shift both by the
  drop delta, PATCH with the current `resourceId`.
- **`eventResize`**: keep `resourceId`, update `startTs` and/or `endTs`.
- **After either:** the mutation's `onSuccess` writes the server response into the
  `["requests"]` cache (optimistic update + rollback already handled by
  `useScheduleRequest`). Conflict state refreshes because `useConflicts()` recomputes
  from the updated scheduled set — no extra call needed.

---

## Opening a request

FullCalendar `eventClick` → `useRequestEditor().open(extendedProps.request)`. This
reuses the existing editor/detail dialogs (`RequestFormDialog` /
`RequestDetailsDialog`); **no new detail screen.**

> UX divergence (intentional): the Space grid opens the editor on **double-click**
> (single-click selects). Calendars conventionally open on **single-click**, and
> FullCalendar has no native double-click. Single-click → open is acceptable and
> matches Outlook/Google/Teams. Drag activation should still require a small movement
> threshold so a plain click doesn't register as a move (FullCalendar handles this).

---

## API (existing — nothing new)

| Need | Existing endpoint | Notes |
|---|---|---|
| Load calendar events | `GET /api/sites/{siteId}/requests?from=&to=` | Site-scoped, returns only scheduled requests whose bar overlaps `[from,to]`. Never loads the whole tenant. |
| Schedule / move / resize | `PATCH /api/requests/{id}/schedule` | Body `ScheduleRequestRequest { resourceId?, startTs?, endTs?, actualDuration* }`. Server applies scheduling rules, recomputes the request, returns updated `RequestInfo`. |
| Backlog for "schedule existing" | `GET /api/requests?scheduled=false` | Unscheduled requests only. |
| Conflicts | computed client-side via `useConflicts()` (which calls the existing validation/`IConflictService` path) | The schedule PATCH returns the updated **request**, not conflict state; conflicts are a **separate** query that recomputes from the refreshed scheduled set. |

The server is responsible for: applying scheduling settings, recomputing the request,
and returning its updated state. Conflict *highlighting* is a client concern layered
on top via `useConflicts()`.

---

## Performance

- Only requests in the **visible (buffered) window** are loaded, via
  `getFetchWindow` + the site/window endpoint.
- Changing the calendar range changes the query key and reloads automatically.
- The calendar shares the scheduled-requests cache with the Space grid, so switching
  tabs within the same window is free.
- Never load the full tenant request history.

---

## UX requirements

- The Calendar must feel familiar to Outlook / Google Calendar / Teams users.
- It is **not** a resource scheduler. Resource planning stays in **Space Utilization**
  and **People Utilization**; the Calendar is strictly a time-oriented scheduling view.
- Light/dark, spacing, borders, and typography must match the existing Utilization
  surfaces via the theming layer above.

---

## Acceptance criteria

### Day view
- Hourly agenda (timeGrid); slot selection opens the Schedule Request chooser; drag
  and resize update schedule only.

### Week view
- Full week (timeGrid); drag and resize work; status + conflict colors render.

### Month view
- Monthly overview (dayGrid); supports request creation (slot select) and opening
  (event click).

### Scheduling
- Existing unscheduled requests can be scheduled from the calendar.
- New requests can be created via the existing `RequestFormDialog`, prefilled from the
  selected slot.
- Moving a request updates `startTs`/`endTs` only (duration preserved).
- Resizing a request updates `startTs`/`endTs` only.

### Resource integrity
- No drag/resize operation changes assigned Spaces, People, or Tools — guaranteed by
  re-sending the current `resourceId` (`getSpaceResourceId`) on every schedule PATCH.

### Consistency (reuse, verified)
- Uses existing conflict definitions (`useConflicts()` / `IConflictService`).
- Uses existing request dialogs (`useRequestEditor`, `RequestFormDialog`,
  `RequestDetailsDialog`).
- Uses existing scheduling validation (server-side `ISchedulingService` via the
  existing PATCH).
- Uses existing permission model (`RequireTenantMembership` server-side; `userCanEdit`
  client-side).
- Adds **no** backend endpoints and **no** new color tokens.

---

## Testing & coverage

Tests live where the code lives (foundation), using the existing stacks — **Vitest +
React Testing Library** (`frontend/vitest.config.ts`, `@vitest/coverage-v8`) on the
frontend and **xUnit + Testcontainers** (`backend/tests/`) on the backend. Because
this feature is almost entirely a new view over existing services, most coverage is
frontend; the backend is exercised by reusing endpoints that already have tests.

### Frontend (required)

Co-locate `*.test.tsx` / `*.test.ts` next to the new code, matching the existing
pattern (e.g. `RequestCalendar.test.tsx` beside `RequestCalendar.tsx`).

- **Event mapping** — pure `RequestInfo → EventInput` mapper unit-tested: title uses
  `name`; only `isScheduled` requests map; `start`/`end` from `startTs`/`endTs`.
- **Color derivation** — base from `getStatusColor(status)`; conflict overlay applied
  when the id is in `useConflicts()`'s error (red) / warning (amber) set; no overlay
  otherwise. Unit-test the class-resolution helper, not FullCalendar internals.
- **Tab integration** — `UtilizationPage` renders the `calendar` tab, persists
  `?tab=calendar`, and gates create/move/resize on `userCanEdit` (assert viewers get a
  read-only calendar). Extend the existing `UtilizationPage` test rather than adding a
  parallel harness.
- **Interaction handlers** (mock FullCalendar's callbacks — don't drive its DOM):
  - `eventDrop` / `eventResize` call `useScheduleRequest` with the **current**
    `resourceId` (assert `getSpaceResourceId` is preserved) and the new times only.
    This is the regression guard for "assignments never change."
  - empty-slot `select` opens the chooser; "create new" opens `RequestFormDialog`
    prefilled with the slot's `startTs`/`endTs`; "schedule existing" lists backlog
    items and schedules with the selected range.
  - `eventClick` opens `useRequestEditor().open(request)`.
- **Loading/window** — changing view updates `scale`/`anchorTs` so `getFetchWindow`
  recomputes the query key (assert the cache key changes / refetch occurs); only
  scheduled requests render.

### Backend (mostly existing)

No new endpoints, so no new endpoint tests are required — `PATCH
/api/requests/{id}/schedule` and `GET /api/sites/{siteId}/requests` already have
coverage under `backend/tests/Endpoints/`. If a request-shape or contract changes,
update `frontend/contracts/*.test.ts` (contract-alignment) accordingly.

### Coverage bar & gates

- New frontend modules (calendar component, mapper, color helper, handlers) should
  meet or exceed the project's existing `@vitest/coverage-v8` threshold; no net
  decrease in coverage.
- `dotnet format` must pass (pre-push hook). Run `npm test` (Vitest) green.
- Because this touches no service signatures or `Map*Endpoints()` functions,
  `./scripts/test-downstream.sh` is **not** required for this feature; run it only if
  the implementation ends up altering a foundation public-API surface.

---

## Out of scope / non-goals

- No resource-row scheduling (that's the Spaces/People tabs).
- No new backend table, endpoint, or `conflictState` field.
- No custom calendar grid implementation.
- No changes to how assignments are created/edited beyond the single
  "schedule from empty slot" case described above.
