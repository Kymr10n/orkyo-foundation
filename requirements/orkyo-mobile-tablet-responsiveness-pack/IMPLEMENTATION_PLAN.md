# Orkyo Mobile & Tablet Responsiveness Implementation Plan

> **Status (verified 2026-08-12): largely implemented.** The plan below was never annotated
> during execution, so it understates what shipped. Phases 2, 3, 4 and 6 are done, and most of
> 7 and 9. The work landed under different names than the plan proposed — see the name map.
>
> | Phase | State |
> |---|---|
> | 1 – Assessment | **Open** — no assessment document exists in this pack |
> | 2 – Responsive foundation | **Done**, renamed (see map) |
> | 3 – Navigation | **Done** — `AppLayout.tsx`: phone `Sheet` drawer, tablet `SidebarNav forceCollapsed` |
> | 4 – Data presentation | **Done** — card mode on ~19 list surfaces |
> | 5 – Request management | **Partly done** — see Phase 9 dialogs |
> | 6 – Calendar | **Done** — `RequestCalendar.tsx` agenda view on phone |
> | 7 – Utilization | **Mostly done** — `UtilizationPage.tsx` drag-free phone agenda; separate mouse/touch sensors |
> | 8 – Floorplans | **Open** — `CollapsibleFloorplan.tsx` has no pinch/zoom/pan or touch handling |
> | 9 – Touch alternatives | **Mostly done** — `ScheduleToDialog.tsx` is the non-drag path |
> | 10 – QA | **Open** — no device-matrix sign-off; `frontend/e2e/mobile.spec.ts` covers part of it |
>
> **Name map** — the plan's proposed primitives versus what exists:
>
> | Planned | Shipped as |
> |---|---|
> | `useBreakpoint()` | `frontend/src/hooks/useBreakpoint.ts` (phone/tablet/desktop at 768/1280) — same name, and the single source of truth for breakpoints |
> | `MobileCardList` | `renderCard` prop on `frontend/src/components/ui/OrkyoDataTable.tsx` |
> | `ResponsiveDialog` | `frontend/src/components/ui/ScaffoldDialog.tsx` |
> | `ResponsivePageLayout`, `ResponsiveToolbar`, `DetailDrawer`, `StickyActionBar` | Not created as named components; the behaviour sits in `AppLayout.tsx` and per-page density passes |
>
> **Remaining work:** Phase 8 floorplan touch, zoom and pan; the Phase 1 assessment document;
> the Phase 10 device-matrix sign-off. This pack is not in `requirements/COMPLETIONS_INDEX.md`.

## Phase 1 – Assessment

### Goal

Identify all desktop-only assumptions.

### Tasks

1. Inventory all pages.
2. Inventory all grids.
3. Inventory all drag/drop interactions.
4. Inventory all hover-dependent actions.
5. Inventory all dialogs.

Deliverable:

- Responsiveness assessment document.

---

## Phase 2 – Responsive Foundation

### Goal

Create shared responsive infrastructure.

### Tasks

Create:

- useBreakpoint()
- ResponsivePageLayout
- ResponsiveToolbar
- MobileCardList
- DetailDrawer
- StickyActionBar
- ResponsiveDialog

Acceptance:

- No page-specific breakpoint logic.

---

## Phase 3 – Navigation

### Goal

Mobile-friendly navigation.

### Tasks

Implement:

- Collapsible side navigation
- Tablet navigation behavior
- Phone navigation behavior

Acceptance:

- Entire application navigable on tablet and phone.

---

## Phase 4 – Data Presentation

### Goal

Responsive presentation of master data.

### Tasks

Extend OrkyoDataTable:

Desktop:
- Existing grid

Tablet:
- Compact grid

Phone:
- Card presentation

Targets:

- Sites
- Spaces
- People
- Tools
- Criteria
- Departments
- Job Titles
- Users

Acceptance:

- No horizontal scrolling required on phones.

---

## Phase 5 – Request Management

### Goal

Full request lifecycle on mobile.

### Tasks

Support:

- Create
- Edit
- View
- Schedule
- Reschedule
- Cancel

Acceptance:

- Complete request workflow on tablet.
- Operational workflow on phone.

---

## Phase 6 – Calendar

### Goal

Responsive scheduling experience.

### Tasks

Implement:

- Responsive day view
- Responsive week view
- Responsive month view
- Agenda view

Acceptance:

- All calendar functions usable without drag/drop.

---

## Phase 7 – Utilization

### Goal

Tablet-friendly planning experience.

### Tasks

Desktop:
- Preserve existing experience

Tablet:
- Touch-friendly timeline

Phone:
- Agenda replacement

Acceptance:

- No broken interactions on touch devices.

---

## Phase 8 – Floorplans

### Goal

Responsive floorplan consumption.

### Tasks

Tablet:

- Zoom
- Pan
- Selection

Phone:

- Read-only mode

Acceptance:

- Stable interaction on iPad and Android tablets.

---

## Phase 9 – Touch Alternatives

### Goal

Remove drag/drop dependency.

### Tasks

Provide dialogs for:

- Schedule request
- Move schedule
- Change duration
- Assign resources
- Remove resources

Acceptance:

- Every drag/drop action has a dialog alternative.

---

## Phase 10 – QA

### Required Test Matrix

Desktop:

- Chrome
- Edge
- Firefox

Tablet:

- iPad Safari
- Android Chrome

Phone:

- iPhone Safari
- Android Chrome

### Validation Areas

- Navigation
- Requests
- Scheduling
- Utilization
- Floorplans
- Settings
- Administration

---

## Success Criteria

Tablet:

- Fully supported operational platform.

Phone:

- View, manage and update work.

Desktop:

- Remains the primary planning experience.

No feature duplication.

No separate mobile codebase.

Single responsive application.
