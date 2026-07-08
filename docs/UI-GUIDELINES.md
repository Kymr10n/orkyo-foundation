# UI guidelines — shared presentation primitives

Canonical rules for the reusable presentation primitives in
`frontend/src/components/ui`. Reach for these instead of hand-rolling the same
markup so empties, statuses, severities and loading states stay consistent
across every product surface.

## Empty states

Use `EmptyState` (`ui/EmptyState.tsx`) for every "nothing here" affordance — a
muted, centered block with an optional leading `icon` and a trailing `action`
(CTA). Do not hand-roll `<div className="text-center py-8 text-muted-foreground">`.

- `message` may be a string or rich node (e.g. a heading + paragraph).
- Pass an `action` when the copy promises one ("Add your first person…" →
  wire the same handler as the page's "Add X" button).
- Pass `className` to tune padding or add a dashed border box, etc.
- `OrkyoDataTable` renders its empty branch through `EmptyState`; supply
  `emptyMessage`, and optionally `emptyIcon` / `emptyAction`.
- Bespoke full-screen empties (e.g. the command palette) may stay hand-rolled.

## Request status

Use `RequestStatusBadge` (`ui/RequestStatusBadge.tsx`) for request-status pills.
It wraps `<Badge className={getStatusColor(status)}>{formatStatusLabel(status)}</Badge>`
(helpers in `lib/utils/utils.ts`) so the tint and the humanised label ("In
Progress", not "in_progress") stay in sync. Never copy that trio inline; pass
`className` for local sizing (e.g. `text-xs flex-shrink-0`).

## Severity presentation

Use `severityPresentation(severity)` (colocated with the helpers in
`ui/status-indicator.tsx`) as the single source for severity icon, colour and
label. It returns `{ icon, iconClass, badgeClass, label }` for `"error"` /
`"warning"`, sharing the red/amber hues with the status indicators and the
request-calendar severity colours. Do not re-declare `getSeverity*` switches.

For the other severity affordances (row indicator, banner, message list, tab
dot) use the existing `StatusIndicator` / `StatusBanner` / `StatusMessageList`
/ `TabIndicatorDot` primitives in the same file.

## Status / role pills

Prefer `Badge` variants (`default`, `secondary`, `destructive`, `warning`,
`success`, `outline`) over hand-rolled `bg-*-100 text-*-800` span pills. Only
fall back to a `className` tint when no variant fits.

## Loading states

Two tiers:

- **Skeletons** for in-region loads where the final layout is known. Use
  `Skeleton` (`ui/skeleton.tsx`): `bg-muted animate-pulse rounded-md`, honouring
  `prefers-reduced-motion`. `OrkyoDataTable` renders column-aware skeleton rows
  in its loading branch (wrapped in `role="status"` with an sr-only label).
- **`LoadingSpinner`** for route/auth/full-region loads (whole page or a whole
  panel with no known shape). Leave these as spinners.

Loading copy uses a single ellipsis character (`…`), never three dots (`...`).
Keep in-region loader messages minimal — only add bespoke copy when it is
genuinely informative ("Loading floorplan…").

## Copy & terminology

These rules govern user-facing display strings only. Identifiers, enum values,
API fields, query keys and code stay as-is (e.g. the customer-account concept is
still `tenant` in code, "Organization" only in copy).

- **Customer account = "Organization".** Never surface "tenant" or "workspace" in
  user-facing copy; both are "organization".
- **Account-holders:** a person is a **member** in an organization context and a
  **user** in the platform-admin (site-admin) context. Keep a single card
  consistent — don't mix "member" and "user" for the same relationship.
- **Product vocabulary:** "request" (not "booking"), "space" (not "room"),
  "people" (not "person resources").
- **Spelling:** en-US throughout display copy — "organize" (not "organise"),
  "standardize", "canceled", "color", "behavior", "analyze". Wire values stay as
  the backend spells them (e.g. the `cancelled` status value) — change only the
  display mapping (`formatStatusLabel`), never the value.
- **Toasts:** sentence case, no trailing period, no "Successfully" prefix —
  "Token revoked", "Imported 3 spaces".
- **Search placeholders:** `Search <things>…` — capital S, real ellipsis (`…`),
  e.g. "Search organizations…".

## Page titles

Every page component calls `usePageTitle(title)` (`hooks/usePageTitle.ts`) with
its own header text; the hook sets `document.title` to `"<title> — Orkyo"` while
mounted and restores `"Orkyo"` on unmount.
