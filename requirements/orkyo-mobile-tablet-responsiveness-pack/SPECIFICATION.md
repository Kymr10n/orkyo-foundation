# Orkyo Mobile & Tablet Responsiveness Specification

## Objective

Improve usability of Orkyo on tablets and mobile devices while preserving desktop as the primary planning experience.

Principles:

- Desktop remains first-class citizen.
- Tablet becomes fully supported.
- Phone becomes operationally usable.
- No native mobile applications.
- Single responsive web application.
- DRY and KISS.
- No duplicate business logic.

---

## Supported Device Classes

### Desktop (Primary)

Resolution:
- >= 1280px

Capabilities:
- Full feature set
- Drag & drop planning
- Floorplan editing
- Administration
- Bulk operations

### Tablet (Supported)

Resolution:
- 600px - 1279px

Capabilities:
- Scheduling
- Calendar usage
- Resource management
- Request management
- Conflict resolution
- Floorplan navigation

Limitations:
- Editing-heavy operations may use dialogs instead of drag/drop.

### Phone (Best Effort)

Resolution:
- < 600px

Capabilities:
- View requests
- Create requests
- Update requests
- Review conflicts
- Check utilization
- View resources

Not optimized for:
- Complex planning workflows
- Large utilization grids
- Floorplan editing

---

## Responsive Breakpoints

```text
Desktop XL: >= 1600px
Desktop:    >= 1280px
Tablet L:   900-1279px
Tablet P:   600-899px
Phone:      <600px
```

All responsive behavior must be centralized.

No page-specific breakpoints.

---

## UX Architecture

### Desktop

Keep existing behavior:

- Utilization grids
- Drag/drop scheduling
- Large data tables
- Split-panel layouts

### Tablet

Switch to:

- Larger touch targets
- Stacked layouts where required
- Responsive toolbars
- Detail drawers
- Dialog-based actions

### Phone

Switch to:

- Card-based layouts
- Agenda views
- Simplified navigation
- Single-column forms

---

## Requests

### Desktop

Existing grid/table behavior remains.

### Tablet

Support:

- List view
- Calendar view
- Detail drawer

### Phone

Default:

- Agenda list
- Request cards
- Quick actions

---

## Utilization Views

### Desktop

Current experience remains unchanged.

### Tablet

Requirements:

- Sticky resource column
- Horizontal scrolling
- Larger row heights
- Tap-to-open dialogs
- Touch-friendly controls

### Phone

Replace timeline by:

- Agenda view
- Resource availability summary
- Conflict summary

---

## Calendar View

Required views:

- Day
- Week
- Month
- Agenda

Mobile defaults:

- Day
- Agenda

All interactions available via dialogs.

No drag/drop-only functionality.

---

## Spaces

### Tablet

Support:

- Space details
- Availability review
- Assignment changes

### Phone

Support:

- Read
- Search
- Availability review

---

## People

### Tablet

Support:

- Availability review
- Skills review
- Assignment actions

### Phone

Support:

- Lookup
- Availability status
- Current assignments

---

## Tools / Equipment

Apply same responsive pattern as People.

---

## Floorplans

### Tablet

Support:

- Zoom
- Pan
- Tap selection
- Resource inspection

### Phone

Support:

- Read-only visualization
- Resource lookup

Not required:

- Editing floorplans

---

## Forms

Requirements:

- Single-column layout on phones
- Responsive sections
- Accordion support
- Sticky action bar
- Large controls
- Inline validation

---

## Navigation

Desktop:
- Existing navigation

Tablet:
- Collapsible navigation

Phone:
- Bottom navigation or hamburger menu

---

## Accessibility

Requirements:

- Minimum touch target 44x44px
- Keyboard support retained
- No hover-only actions
- Focus visibility
- Responsive typography

---

## Component Strategy

Create reusable responsive components.

Required components:

- ResponsivePageLayout
- ResponsiveToolbar
- ResponsiveDataView
- MobileCardList
- DetailDrawer
- StickyActionBar
- TouchActionMenu
- ResponsiveDialog

No duplicated implementations.

All modules must consume shared components.
