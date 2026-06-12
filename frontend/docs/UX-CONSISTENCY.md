# UX Consistency & Friction Audit — Foundation Frontend

> **Status:** Audit + remediation proposal, with the **additive P0 primitives now implemented**
> (see [What landed](#what-landed-in-this-change)). It records UX friction found in `frontend/`,
> explains *why* each one bites the user, and proposes staged, costed remediation. The larger
> call-site refactors (P1/P2) are deliberately deferred — see
> [Remediation backlog](#remediation-backlog).

## Purpose & scope

The trigger for this audit: **scrolling is incoherent across the app.** On some screens a grid
scrolls inside a fixed frame; on others the whole page scrolls; on others a dialog scrolls — and
on at least one screen the *same page* scrolls two different ways depending on which view is
selected. This document catalogs that and the adjacent inconsistencies that cause it, then
proposes a single, simple model to converge on.

"Consistency" here means: **a user can predict, before they touch the trackpad, what will move.**
That predictability is the product of two things this codebase currently lacks — a stated
*scroll-ownership rule*, and *shared primitives* that make the rule the path of least resistance.

`frontend/` is a React 19 + Vite + Tailwind + Radix (shadcn) package (`@kymr10n/foundation`)
consumed by **both** `orkyo-saas` and `orkyo-community`. Per the repo's `CLAUDE.md`, changes to
the public component API are backward-compatibility–sensitive: breaking changes require a major
version bump + coordinated downstream PRs, gated by `scripts/test-downstream.sh`. The remediation
backlog below tags every item with whether it crosses that line, so the additive fixes can land
without the heavyweight coordination.

All file:line references were verified against the tree at the time of writing; see
[How these were found](#how-these-were-found) for the full anchor list.

---

## The scroll model we *should* have

One rule, stated plainly (KISS):

> **Exactly one scroll owner per route.**
>
> 1. The app frame owns page scroll. `AppLayout`'s `<main className="flex-1 overflow-auto p-4">`
>    (`components/layout/AppLayout.tsx:93`) is the default scroll surface. Most pages should let
>    it do the scrolling and not introduce their own.
> 2. A page opts **out** of page scroll *only* when it hosts a single self-contained scroll
>    surface — a timeline grid, a virtualized tree, a data table that manages its own viewport.
>    In that case **that surface is the sole scroll owner**, and every ancestor up to `<main>` is
>    `overflow-hidden` and pairs `flex-1` with `min-h-0` so the surface is height-bounded.
> 3. Never two competing owners (page *and* grid). Never a third behavior selected per view mode.

Containment diagram:

```
┌─ AppLayout  h-screen flex flex-col ──────────────────────────────┐
│  TopBar  (sticky top-0, never scrolls)                           │
│ ┌─ flex-1 flex overflow-hidden ───────────────────────────────┐ │
│ │ Sidebar │  <main> flex-1 overflow-auto   ◀── DEFAULT OWNER   │ │
│ │         │  ┌────────────────────────────────────────────┐   │ │
│ │         │  │ Page                                        │   │ │
│ │         │  │   • simple page → let <main> scroll. done.  │   │ │
│ │         │  │   • page with a scroll surface:             │   │ │
│ │         │  │       page = overflow-hidden                │   │ │
│ │         │  │       every flex level = flex-1 + min-h-0   │   │ │
│ │         │  │       ┌──────────────────────────────────┐  │   │ │
│ │         │  │       │ Grid / Tree / Table  ◀ SOLE OWNER │  │   │ │
│ │         │  │       │  overflow-auto, height-bounded    │  │   │ │
│ │         │  │       └──────────────────────────────────┘  │   │ │
│ │         │  └────────────────────────────────────────────┘   │ │
│ └─────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
```

The **`min-h-0`** in rule 2 is not optional polish — it is the single most common reason scroll
containment silently breaks (see [Finding D](#d-the-min-h-0-footgun)). A flex item defaults to
`min-height: auto`, which refuses to shrink below its content, so `flex-1` alone lets a tall
child blow past the viewport and hand scrolling back to the page. `min-h-0` is what makes the
child actually bounded.

---

## Findings

Each finding: **symptom → evidence → why it bites the user → remediation.** Scroll findings
(A–E) lead because they are the reported problem; the DRY debt (F) is last because it is the
*root cause* that let A–C drift in the first place.

### A. Scroll ownership is decided ad-hoc, page by page

**Symptom.** The user's exact observation: sometimes the grid scrolls, sometimes the page,
sometimes a dialog. There is no rule, so each page improvises.

**Evidence.**
- The frame defines a perfectly good default owner: `AppLayout.tsx:89-95` — `h-screen flex flex-col`
  with `<main className="flex-1 overflow-auto p-4">`.
- But pages override it inconsistently. The clearest offender, `RequestsPage.tsx:554`, **scrolls
  two different ways within one screen depending on view mode**:
  ```tsx
  <div className={`flex-1 ${selectedRequest ? 'min-w-0' : ''} ${
    viewMode === 'tree' ? 'overflow-hidden' : 'min-h-0 overflow-y-auto'}`}>
  ```
  Tree view → `overflow-hidden` (the virtualized tree owns scroll). List view → `min-h-0
  overflow-y-auto` (the column owns scroll). Same data, same page, two scroll behaviors.
- Containment helpers disagree on the contract: `PageLayout.tsx:11` uses `h-full`, `PageTabs.tsx:28`
  uses `flex-1 min-h-0`, and several pages use neither — so "who owns the scroll" is re-decided
  from scratch every time.

**Why it bites.** Muscle memory breaks. A user who learned "scroll moves the list" on one screen
finds it moves the whole page on the next, and toggling a view mode silently changes the rule.
Scroll-position is also lost or duplicated when ownership flips.

**Remediation.** Adopt the [one-scroll-owner rule](#the-scroll-model-we-should-have) as written
convention now; encode it in a `PageShell`/`ScrollContainer` primitive (P1) so pages physically
can't forget it. Collapse `RequestsPage`'s tree/list to a single behavior — pick "surface owns
scroll" for both, since the list already virtualizes elsewhere.

### B. Every dialog reinvents its overflow strategy

**Symptom.** Tall dialogs scroll differently — and clip differently — depending on which dialog
you opened.

**Evidence.** The base `DialogContent` sets **no** height bound and **no** overflow
(`components/ui/dialog.tsx:35` — the class string has neither `max-h` nor `overflow`). So each
caller invents one, and three mutually incompatible patterns have emerged:

| Pattern | Example | Approach |
|---|---|---|
| A | `RequestFormDialog.tsx:296` → `:316`; `TemplateDialogBase.tsx:176` | `max-h-[85vh] flex flex-col p-0` shell + **inner** `overflow-y-auto` body |
| B | `PersonAssignmentDialog.tsx:349` → `:365` | `max-h-[80vh] overflow-hidden` shell + **nested** `<ScrollArea max-h-[55vh]>` |
| C | `AvailabilityEventDialog.tsx:495` | `max-h-[90vh] overflow-y-auto` **directly** on `DialogContent` |

The viewport caps are unowned magic numbers: **55vh / 80vh / 85vh / 90vh**, chosen per dialog.

**Why it bites.** Inconsistent max heights mean a dialog jumps to a different size than the last
one for no user-visible reason. Pattern C scrolls the header/title with the body; patterns A and
B pin the header — so whether the title stays visible while you scroll a long form is pure luck.
Pattern B's nested `55vh` inside `80vh` can produce a double scrollbar.

**Remediation (P0, additive).** Give `DialogContent` a sane default — e.g.
`max-h-[85dvh] flex flex-col` — and add a `ScrollableDialogBody` helper that is the *only*
sanctioned scroll region (header/footer pinned, body scrolls). Existing callers keep working
(they pass `className`, which still merges); new ones get the right behavior for free. Prefer
extending the **already-existing** `FormDialog` (`components/ui/FormDialog.tsx`) over adding a
parallel primitive. Use `dvh` not `vh` so mobile browser chrome doesn't clip the dialog.

### C. Scrollbar affordance is inconsistent inside the timeline grid

**Symptom.** In the utilization timeline you can scroll horizontally but get no scrollbar telling
you there's more, and no sign when you've hit the end.

**Evidence.** `components/utilization/TimelineGridShell.tsx`: the header track is
`overflow-x-auto overflow-y-hidden scrollbar-hide` (`:169`) — scrollable but the bar is
*hidden* — while the body is `overflow-y-auto overflow-x-auto` with a *visible* bar (`:205`). The
two are kept in lockstep by manual JS handlers (`onScroll={handleHeaderScroll}` `:170`,
`onScroll={handleBodyScroll}` `:206`).

**Why it bites.** No end-of-range affordance on the header axis; the user can't tell the timeline
extends further. And any hiccup in the JS sync (fast fling, reflow) visibly desyncs the header
labels from the body cells — columns no longer line up with their data.

**Remediation (P2).** Standardize on a single visible (or single hidden) scrollbar treatment for
the coupled axis, and prefer CSS `position: sticky` for the header over JS scroll-mirroring where
the layout allows, removing the desync class of bug entirely.

### D. The `min-h-0` footgun

**Symptom.** A container that "should" scroll internally instead pushes the whole page taller.

**Evidence.** Correct usage is scattered and hand-applied: `PageTabs.tsx:28`,
`RequestsPage.tsx:551,554`, `ConflictsPage.tsx:260`, `RequestTreeView.tsx:667` — each independently
remembers to write `min-h-0`. Nothing enforces it, so any new nested-flex scroll area that omits
it breaks silently.

**Why it bites the developer (and then the user).** It's invisible in review — the markup looks
right, the bug only appears when content overflows — so it ships, and the user gets a page that
scrolls when a panel should have.

**Remediation (P1).** Bake `flex-1 min-h-0` into the `PageShell`/`ScrollContainer` primitive from
Finding A so the correct containment is structural, not a thing each author must recall. Add it to
the [cheat-sheet](#conventions-cheat-sheet) in the meantime.

### E. `ScrollArea` forwards its ref to the wrong element

**Symptom.** The shared `ScrollArea` can't be used as a virtualization scroll container, forcing
plain-`<div>` workarounds and undermining the point of having a shared component.

**Evidence.** `components/ui/scroll-area.tsx:9-12` — the `forwardRef` lands on
`ScrollAreaPrimitive.Root`, but the element that actually scrolls is the inner `Viewport`
(`:15`). `@tanstack/react-virtual` needs a ref to the scrolling element. This is already a known,
documented footgun in the codebase — `RequestTreeView.tsx:664-665` carries an inline comment:
> *"A Radix ScrollArea forwards its ref to the non-scrolling Root, not the inner Viewport, which
> mis-wires [the virtualizer]"* — and works around it with a raw `<div ref={parentRef}>` at `:667`.

**Why it bites.** Inconsistency by omission: virtualized surfaces (the big grids/trees — exactly
the screens with the most scroll content) can't use the house scroll styling, so their scrollbars
look and behave differently from everywhere else, feeding Finding A.

**Remediation (P0, additive — done).** Added an optional `viewportRef` prop forwarding to the
Viewport so `ScrollArea` is usable as a virtualization container (purely additive). `RequestTreeView`
is migrated onto it (`type="auto"` for a native-like, always-when-overflowing scrollbar), removing
the plain-`<div>` workaround. Other virtualized surfaces (`RequestsPanel`, `ConflictsPage`) can
follow the same pattern.

### F. DRY debt is the root cause that let A–C drift

**Symptom.** ~30 dialogs hand-roll the same Dialog + error + footer skeleton, and because each is
hand-rolled, each made its own overflow decision (Finding B) and its own spacing/size choices.

**Evidence.**
- `components/ui/FormDialog.tsx` already standardizes the Dialog+error+footer composite, yet the
  pattern is reimplemented across `settings/`, `requests/`, `spaces/`, `people/`, `utilization/`,
  and `resource-groups/` (e.g. `CreateSiteDialog`, `EditSiteDialog`, `PersonEditDialog`,
  `ResourceGroupEditDialog`, …). Divergent overflow handling is a *direct symptom* of this.
- **No design tokens.** Spacing (`gap-1/2/4`, `space-y-1.5/3/4`), radius (`rounded-sm/md/lg/2xl`),
  and hardcoded sizes (`sm:max-w-[500px]`, `w-[70px]`, `min-h-[80px]`, the 55–90vh dialog caps)
  are scattered literals, not named tokens. There is no Tailwind theme extension; only the
  shadcn CSS variables in `index.css`.
- **`TooltipProvider` mounted per-component** — `AutoScheduleButton.tsx:18`,
  `FeedbackButton.tsx:94`, `SettingRow.tsx` (+ others) each wrap their own, instead of one
  provider at the app root. Inconsistent `delayDuration` follows from this (e.g. `300` in one
  place, default elsewhere).
- No component docs / Storybook; no a11y tests (`jest-axe`); `LoadingSpinner` lacks `aria-busy`
  and screen-reader text.

**Why it bites.** Every new CRUD surface starts from a blank dialog and re-derives layout,
overflow, spacing, and footer placement — so consistency is left to chance, which is how A–C
happened. This is the DRY/KISS core of the whole report: *the inconsistencies are downstream of
missing shared primitives.*

**Remediation.** Drive new dialogs through `FormDialog` (reuse, not rebuild); migrate the worst
divergers opportunistically. Introduce a small design-token layer (Tailwind theme extension or a
`constants/design-tokens.ts`) for the spacing/radius/size/`dvh` literals. Hoist `TooltipProvider`
to the app root and delete the per-component providers.

---

## Remediation backlog

Prioritized and costed. **"Major bump?"** = does it change the public component API such that
`CLAUDE.md` requires a major version + coordinated downstream PRs (gated by
`scripts/test-downstream.sh`)?

| # | Item | DRY / KISS rationale | Blast radius | Major bump? | Downstream PRs? |
|---|------|----------------------|--------------|-------------|-----------------|
| **P0 ✅** | Document the one-scroll-owner rule (this file + cheat-sheet) | One rule replaces N ad-hoc decisions | Docs only | No | No |
| **P0 ✅** | `ScrollableDialogBody` helper + `FormDialog` height-bounded with scrolling body | Kills Finding B's 3 patterns + magic vh | Additive helper; `FormDialog` callers improved in place | No (additive) | No |
| **P0 ✅** | `ScrollArea` `viewportRef` (forward to Viewport) | Removes Finding E workarounds | Additive prop | No (additive) | No |
| **P0** | Route new dialogs through `FormDialog`; migrate worst hand-rolled overflow divergers | Reuse over rebuild (Finding F) | New code + opportunistic migration | No | No |
| **P0 (deferred)** | Safe default `max-h`/`overflow` on the *base* `DialogContent` | Protects raw-`Dialog` users too | Touches every dialog — needs visual QA across the 3 patterns | No (additive) | No |
| **P0 ✅** | Height-bound the base `DialogContent` (`flex flex-col max-h-[85dvh]`, no base overflow) | Removes per-dialog `max-h` guesswork (Finding B) | All dialogs (visual-QA-friendly) | No (additive) | No |
| **P1 ✅** | Migrate `RequestsPanel` + `ConflictsPage` to `ScrollArea` + `viewportRef` | Consistent house scrollbar (Finding E) | Two surfaces | No | No |
| **P1 ✅** | Collapse `RequestsPage` tree/list to one scroll behavior | Removes the per-view-mode surprise (Finding A) | One page | No | No |
| **P1** | `PageShell` / `ScrollContainer` primitive encoding `flex-1 min-h-0` + overflow contract | Makes Findings A & D structural, not recalled | Pages migrate onto it | Only if exported & signatures shift | If exported from package |
| **P2 ✅** | Standardize tooltip `delayDuration` to 300ms (kept self-contained — see note) | Consistent delay (Finding F) | A few components | No | No |
| **P2 ✅** | a11y: `LoadingSpinner` `role/aria-busy`/SR text + `jest-axe` harness | Consistency the eye can't see | Tests + small component edits | No | No |
| **P2 (needs visual QA)** | `TimelineGridShell` sticky-header over JS scroll-sync | Removes Finding C desync class | One virtualized grid — high regression risk | No | No |
| **P2 (downstream)** | Shared Tailwind *theme* token scale | Names spacing/radius literals (Finding F) | Products only — foundation has no Tailwind build | n/a here | Lives in saas/community |

> **Tooltip note:** the original backlog said "hoist `TooltipProvider` to a single root." Rejected
> for this repo: it's a shared **library**, Radix 1.2.8 *requires* a `TooltipProvider` ancestor, so
> a single root provider would break any downstream product that renders these components outside
> `TenantApp`. Kept each provider self-contained and standardized the delay instead.

**Recommended sequence.** Land all **P0** first — they are additive, break nothing, and remove
the *causes* of the friction (no scroll rule, no dialog default, unusable shared `ScrollArea`,
unused `FormDialog`). **P1** is where the public-API question appears: if `PageShell` is exported
from the package, treat it as a coordinated change (major bump + downstream PRs +
`scripts/test-downstream.sh`); if it stays internal to foundation's own pages, it's a normal
change. **P2** is consistency polish, all internal.

---

## What landed in this change

The additive, no-API-break P0 primitives are implemented. They remove the *causes* of the
friction without touching the ~30 call sites (deferred to P1/P2 so they can get visual QA and,
where exported, the `CLAUDE.md` coordination):

- **`ScrollableDialogBody`** (`components/ui/dialog.tsx`, exported from `components/ui/index.ts`) —
  the single sanctioned dialog scroll region (pinned header/footer, body scrolls). Replaces the
  three divergent per-dialog overflow recipes from [Finding B](#b-every-dialog-reinvents-its-overflow-strategy).
- **`FormDialog` is now height-bounded** (`components/ui/FormDialog.tsx`) — `max-h-[85dvh] flex
  flex-col` with its body in `ScrollableDialogBody`. Every dialog routed through `FormDialog` now
  scrolls correctly instead of pushing the page or clipping; short dialogs are visually unchanged.
- **`ScrollArea` gained `viewportRef`** (`components/ui/scroll-area.tsx`) — forwards a ref to the
  scrolling Viewport so virtualizers stop needing the plain-`<div>` workaround from
  [Finding E](#e-scrollarea-forwards-its-ref-to-the-wrong-element). Covered by
  `components/ui/scroll-area.test.tsx`.
- **`RequestTreeView` migrated onto `ScrollArea` + `viewportRef`** (`components/requests/RequestTreeView.tsx`) —
  the primary virtualized tree now uses the house scrollbar (`type="auto"`) instead of a bare
  overflow div; the workaround comment is gone.
- **Coding guidelines** captured in [`UI-GUIDELINES.md`](UI-GUIDELINES.md) so future work follows
  the scroll-owner rule, the dialog recipe, and the virtualization pattern by default.

All additive: existing `ScrollArea`/`Dialog` callers are untouched; typecheck, lint, and the full
suite pass. A follow-up change extended this work — `RequestsPanel`/`ConflictsPage` migrated onto
`viewportRef`, `RequestsPage` unified to a single scroll owner, the base `DialogContent` made
height-bounded by default, `LoadingSpinner` a11y, a `jest-axe` harness, and standardized tooltip
delays — see the ✅ rows in the [backlog](#remediation-backlog). Remaining: the `PageShell`
primitive, the `TimelineGridShell` sticky-header rewrite (needs visual QA), and a downstream
Tailwind token scale.

---

## Conventions cheat-sheet

The canonical, example-backed version lives in [`UI-GUIDELINES.md`](UI-GUIDELINES.md). Quick recap:

- **Scroll owner:** one per route. Default = let `AppLayout`'s `<main>` scroll. Introduce an
  internal scroll surface only when the page hosts a grid/tree/table — then make the page
  `overflow-hidden` and the surface the sole owner. Never two owners; never per-view-mode.
- **`flex-1` is always paired with `min-h-0`** on any flex child that must scroll internally.
  If you write `flex-1 overflow-*`, you almost certainly also need `min-h-0`.
- **Dialogs:** reach for `FormDialog` before a raw `Dialog`. Let the default `max-h`/`overflow`
  do the work; put scrolling content in the sanctioned `ScrollableDialogBody`, not on
  `DialogContent`. Use `dvh`, not `vh`. Don't invent a new `max-h-[..vh]` per dialog.
- **Virtualized lists:** pass the `ScrollArea` `viewportRef` to the virtualizer; don't fall back
  to a raw `<div>` for styling reasons.
- **Magic numbers:** use a named token, not a literal, for spacing/radius/size/dialog-height.
- **Tooltips:** assume a single root `TooltipProvider`; don't mount your own.

---

## How these were found

Anchors for spot-checking (all verified against the tree at audit time):

| Finding | Files:lines |
|---|---|
| A — ad-hoc scroll ownership | `components/layout/AppLayout.tsx:89-95`; `pages/RequestsPage.tsx:551,554`; `components/layout/PageLayout.tsx:11`; `components/layout/PageTabs.tsx:28` |
| B — dialog overflow patterns | `components/ui/dialog.tsx:35`; `RequestFormDialog.tsx:296,316`; `TemplateDialogBase.tsx:176`; `PersonAssignmentDialog.tsx:349,365`; `AvailabilityEventDialog.tsx:495` |
| C — scrollbar affordance | `components/utilization/TimelineGridShell.tsx:169,170,205,206` |
| D — `min-h-0` footgun | `PageTabs.tsx:28`; `RequestsPage.tsx:551,554`; `ConflictsPage.tsx:260`; `RequestTreeView.tsx:667` |
| E — `ScrollArea` ref | `components/ui/scroll-area.tsx:9-12,15`; `RequestTreeView.tsx:664-665,667` |
| F — DRY debt | `components/ui/FormDialog.tsx`; `index.css` (tokens); `AutoScheduleButton.tsx:18`; `FeedbackButton.tsx:94`; `SettingRow.tsx` |

Method: three parallel read-only sweeps of `frontend/src` — (1) layout/scroll architecture,
(2) every `overflow`/height/scrollbar usage, (3) shared-component inventory & duplication —
cross-checked by reading the cited files directly.
