# Frontend UI Guidelines

Canonical, prescriptive rules for building UI in `@kymr10n/foundation`. Keep these in force for
every new component and every change you touch. The *why* behind them lives in
[`UX-CONSISTENCY.md`](UX-CONSISTENCY.md); this file is the *what to do*.

Principles: **DRY** (reach for the shared primitive before writing a new one) and **KISS**
(one predictable rule beats N clever per-screen decisions).

---

## 1. Scrolling — one owner per route

**Rule:** exactly one element scrolls per route. By default that element is the app frame's
`<main>` (`components/layout/AppLayout.tsx`). A page introduces its own scroll surface *only*
when it hosts a single self-contained region (grid / virtualized tree / data table). When it
does, that region is the **sole** scroll owner and every ancestor up to `<main>` is
`overflow-hidden` and height-bounded.

**Never:** two scroll owners on one screen, or a different scroll behavior per view mode/tab.

```tsx
// ✅ Simple page — let the frame scroll. Don't add overflow.
<PageLayout>
  <PageHeader title="Settings" />
  <SettingsForm />
</PageLayout>

// ✅ Page with a self-contained scroll surface — page is overflow-hidden,
//    the surface owns scroll, every flex level carries `min-h-0`.
<div className="flex h-full flex-col overflow-hidden">
  <Toolbar />
  <div className="flex min-h-0 flex-1">
    <RequestTreeView /> {/* owns its own scroll */}
  </div>
</div>

// ❌ Don't switch scroll ownership by view mode (the RequestsPage bug).
<div className={mode === 'tree' ? 'overflow-hidden' : 'overflow-y-auto'}>…</div>
```

## 2. `flex-1` is always paired with `min-h-0`

A flex child defaults to `min-height: auto` and refuses to shrink below its content, so `flex-1`
alone lets a tall child overflow and hand scrolling back to the page. If a flex child must scroll
internally, it needs **both**.

```tsx
// ✅                                   // ❌ silently overflows once content is tall
<div className="flex-1 min-h-0 overflow-y-auto" />   <div className="flex-1 overflow-y-auto" />
```

Rule of thumb: if you write `flex-1` together with any `overflow-*`, you almost certainly also
need `min-h-0`.

## 3. Dialogs

- **Reach for `FormDialog` before a raw `Dialog`** for any "open → fill a form → submit" flow.
  It owns the shell, error display, footer, height-bounding, and body scroll — don't re-roll them.
- For non-form dialogs that may get tall, build on `DialogContent` as a **height-bounded flex
  column** and put scrolling content in **`ScrollableDialogBody`** — the *one* sanctioned scroll
  region (pinned header/footer, scrolling body).
- Use **`dvh`**, not `vh`, for dialog height caps (keeps clear of mobile browser chrome).
- **Don't** invent a per-dialog `max-h-[..vh]`, and **don't** put `overflow` directly on
  `DialogContent` or nest a second `ScrollArea` inside it.

```tsx
// ✅ Form dialog — nothing to configure; tall forms scroll automatically.
<FormDialog open={open} onOpenChange={setOpen} title="Edit site"
  onSubmit={save} isSubmitting={saving} submitLabel="Save">
  <SiteFields />
</FormDialog>

// ✅ Non-form dialog with tall content.
<DialogContent className="flex max-h-[85dvh] flex-col gap-0">
  <DialogHeader className="shrink-0"><DialogTitle>Members</DialogTitle></DialogHeader>
  <ScrollableDialogBody className="py-4">{rows}</ScrollableDialogBody>
  <DialogFooter className="shrink-0 pt-4">{actions}</DialogFooter>
</DialogContent>

// ❌ Each of these is a divergent overflow recipe — don't.
<DialogContent className="max-h-[90vh] overflow-y-auto">…</DialogContent>
<DialogContent className="max-h-[80vh] overflow-hidden"><ScrollArea className="max-h-[55vh]">…</ScrollArea></DialogContent>
```

## 4. Virtualized lists & `ScrollArea`

A virtualizer (`@tanstack/react-virtual`) must scroll the element you give its
`getScrollElement`. The shared `ScrollArea` scrolls its inner **Viewport**, not the Root that the
default `ref` targets — so pass the virtualizer's ref via **`viewportRef`**, and use the house
`ScrollArea` instead of a bare `overflow` div so the scrollbar matches the rest of the app.

```tsx
const parentRef = useRef<HTMLDivElement>(null);
const virtualizer = useVirtualizer({ getScrollElement: () => parentRef.current, /* … */ });

// ✅ House scrollbar + correctly-wired virtualizer.
<ScrollArea type="auto" viewportRef={parentRef} className="flex-1 min-h-0">
  <div style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>{rows}</div>
</ScrollArea>

// ❌ Forwarding the normal ref to ScrollArea mis-wires the virtualizer (it lands on the
//    non-scrolling Root); a bare overflow div works but uses an off-brand scrollbar.
<ScrollArea ref={parentRef}>…</ScrollArea>
```

Use `type="auto"` for primary content surfaces so the bar is visible whenever content overflows
(native-like), rather than the hover-only default.

## 5. Tokens over magic numbers

Tailwind's built-in scale **is** the token system — prefer `gap-2`, `rounded-md`, `text-sm` over
arbitrary `[..]` values. Avoid one-off `w-[70px]` / `min-h-[80px]` / `max-h-[83vh]` literals.

When a value genuinely must be app-specific (e.g. the dialog height cap), **centralize it in the
component that owns it** so there's a single source of truth — e.g. `max-h-[85dvh]` lives once on
`DialogContent`, and the form-dialog width once on `FormDialog`. Don't copy such literals into
call sites.

> Note: `orkyo-foundation` ships source + `index.css` with raw `@tailwind` directives and has **no
> Tailwind build of its own** — the consuming products (saas/community) run Tailwind. So a shared
> Tailwind *theme* extension (custom spacing/radius scales) is a downstream concern, not something
> this repo can add. Foundation's own tokens are: Tailwind's default scale, the CSS variables in
> `index.css` (colors, `--radius`), and centralized component constants.

## 6. Tooltips

Assume a single `TooltipProvider` near the app root; don't mount a new provider per component, and
don't set per-component `delayDuration` overrides.

## 7. Status, severity & feedback colours

Foundation has **no Tailwind theme of its own** (§5), so there are no `bg-warning`/`bg-success`
semantic tokens — the single source of truth is the shared primitives and colour helpers below.
**Never hand-roll a feedback box from a raw `<div>` + a light semantic background, and never
hardcode a semantic colour class on a `Badge`.** Reach for the matching pattern instead:

| Need | Use |
|---|---|
| Section-level message (load error, warning panel) | `<Alert variant="destructive\|warning\|default">` + icon as first child |
| Compact inline form error | `<ErrorAlert message={…} />` |
| Row-level validation list | `<ValidationIssueList variant="blocker\|warning">` |
| Status / severity tag | `<Badge variant="success\|warning\|destructive\|secondary">` — never a colour className |
| Status colour in bespoke markup | the `getStatusColor` / `getStatusDotColor` helpers (`lib/utils`) |
| Calendar / Gantt severity colour | `SEVERITY_EVENT_CLASS` / `SEVERITY_SWATCH` (`utilization/request-calendar-events.ts`) |

**One palette for status, everywhere.** A status reads the same in a list badge and on the
calendar: `planned` → blue, `in_progress` → **amber**, `done` → **emerald**, `cancelled` → muted.
`getStatusColor` (badges) and `getCalendarEventColor` (calendar) are locked to the same colour
family by a cross-helper test in `request-calendar-events.test.ts` — if you change one palette,
change both and keep that test green.

```tsx
// ✅ section error            // ✅ compact form error        // ✅ semantic tag
<Alert variant="destructive">  <ErrorAlert message={error} /> <Badge variant="warning">Conflict</Badge>
  <AlertCircle className="h-4 w-4" />
  <AlertDescription>{msg}</AlertDescription>
</Alert>

// ❌ hand-rolled feedback box — the lint rule (below) rejects this.
<div className="bg-destructive/10 text-destructive p-3 rounded-md">{error}</div>
<Badge variant="outline" className="text-amber-600 border-amber-300">Conflict</Badge>
```

**Enforcement.** `eslint.config.js` carries a `no-restricted-syntax` rule (scoped to
`src/components/**`) that fails the build on `bg-destructive/10` and light semantic backgrounds
(`bg-{red,amber}-50`) in component markup. The colour-source primitives themselves
(`ui/alert.tsx`, `ui/badge.tsx`, `ui/ErrorAlert.tsx`, `ui/ValidationIssueList.tsx`) and two
deliberate, non-box uses (`RequestCalendar.tsx`, `TimelineGridShell.tsx`, `BreakGlassBanner.tsx`)
are allowlisted there. A genuine one-off needs a justified `// eslint-disable-next-line` — not a
new hand-rolled box.

---

## 8. Third-party widgets & the Tailwind v4 cascade-layer rule

Tailwind v4 emits all utilities inside `@layer utilities`. Per the CSS cascade, **unlayered styles
beat layered ones regardless of specificity or source order.** So when a third-party widget injects
its *own* unlayered CSS at runtime (e.g. FullCalendar v6's `.fc-event` rules) or ships an unlayered
stylesheet, that CSS will **win over any Tailwind utility class** you put on the same element — the
utility silently does nothing (transparent backgrounds, missing borders, etc.). This worked under
Tailwind v3 (utilities weren't natively layered) and regressed on the v4 upgrade.

**Rule:** when you style a third-party widget that injects/ships unlayered CSS, the utilities that
must override it need the `!` important modifier (`bg-blue-100!`, `dark:bg-blue-950!`). `!important`
beats unlayered non-important declarations. Scope it to the elements the widget also styles — don't
blanket the whole component.

Reference implementation: `components/utilization/request-calendar-events.ts` (`getCalendarEventColor`
+ `SEVERITY_EVENT_CLASS`) — the `!` modifiers there exist solely for this reason, with a comment.

**Safe alternative:** CSS-*variable* theming is immune — the widget's own rule reads your var, so
setting `--fc-…`/`--rdp-…` design-token values (see `request-calendar.css`) never fights the cascade.
Prefer variable theming where the widget exposes it; reach for `!` only to override a concrete
property the widget hard-sets. (Widgets that are fully restyled via `className` props with no shipped
CSS — e.g. react-day-picker as wrapped in `ui/calendar.tsx`, sonner via its `theme`/`richColors`
mechanism — are not at risk and need no `!`.)

---

## Public-API note (this is a shared package)

`@kymr10n/foundation` is consumed by `orkyo-saas` and `orkyo-community`. Per the repo `CLAUDE.md`,
changes to exported component signatures must stay backward-compatible within a major version;
breaking changes require a major bump **and** coordinated downstream PRs (run
`scripts/test-downstream.sh`). Adding an **optional** prop (as `viewportRef` did) is additive and
safe; removing/renaming props or changing required shapes is not.
