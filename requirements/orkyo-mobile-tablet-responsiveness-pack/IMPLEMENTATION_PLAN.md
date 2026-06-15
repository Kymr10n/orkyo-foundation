# Orkyo Mobile & Tablet Responsiveness Implementation Plan

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
