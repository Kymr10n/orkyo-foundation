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

Prefer a named token/utility over a raw literal for spacing, radius, and sizes. Avoid one-off
`w-[70px]` / `min-h-[80px]` / `max-h-[83vh]` values; if a value must be specific, factor it into
a shared constant so it stays consistent across screens. (A design-token layer is on the backlog
in [`UX-CONSISTENCY.md`](UX-CONSISTENCY.md#remediation-backlog); until it lands, reuse existing
utilities and don't invent new magic numbers.)

## 6. Tooltips

Assume a single `TooltipProvider` near the app root; don't mount a new provider per component, and
don't set per-component `delayDuration` overrides.

---

## Public-API note (this is a shared package)

`@kymr10n/foundation` is consumed by `orkyo-saas` and `orkyo-community`. Per the repo `CLAUDE.md`,
changes to exported component signatures must stay backward-compatible within a major version;
breaking changes require a major bump **and** coordinated downstream PRs (run
`scripts/test-downstream.sh`). Adding an **optional** prop (as `viewportRef` did) is additive and
safe; removing/renaming props or changing required shapes is not.
