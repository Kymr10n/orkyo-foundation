# Cross-engine smoke suite (measure M7)

A minimal Playwright suite that renders a slice of real foundation components
(FormDialog, OrkyoDataTable, TimelineGridShell + off-time hatch, RequestCalendar,
ScheduleToDialog) against fixture data, across **chromium / webkit / firefox**.
Its job is narrow: catch parse errors from too-new syntax and gross cross-engine
render breakage. Component *logic* is covered by the vitest suite.

## Not a CI gate

This is **on-demand only** and deliberately **not wired into CI**. Grow it only
when a real cross-engine bug surfaces — one spec file, one Playwright config.

If automation is ever wanted, the right shape is a **manual `workflow_dispatch`**
GitHub Action that runs `npm run test:browsers` / `npm run test:mobile` — do NOT
add either lane to the PR CI gate.

## Two lanes

- **Desktop cross-engine** (`chromium` / `firefox` / `webkit`) — runs every
  `*.spec.ts` **except** `mobile.spec.ts`. `npm run test:browsers`.
- **Mobile/tablet viewport** (`mobile-webkit` = iPhone 14, `mobile-chromium` =
  Pixel 7, `tablet-webkit` = iPad gen 7) — runs **only** `mobile.spec.ts`, which
  asserts the phone/tablet responsiveness behaviours that vitest can't see because
  they depend on a real viewport + `matchMedia`: OrkyoDataTable card mode vs table,
  the dvh-capped FormDialog footer, the horizontally-scrollable wizard tab strip,
  and the compressed month-grid calendar on phone. `npm run test:mobile`.

`mobile.spec.ts` deliberately does **not** cover the TopBar phone-overflow menu:
the fixture-only harness doesn't mount TopBar, and stubbing its AuthContext /
`useSites` / router surface would need several mock modules (more than the ~1
focused mock the plan budgets for). That behaviour is covered by the TopBar vitest
(`src/components/layout/TopBar.test.tsx`) instead.

> Status note: these mobile projects are authored but may be **unrun** in
> environments where Playwright browsers can't launch (no system libs / sandbox).
> They are on-demand; run them locally with the commands below.

## How it works

Foundation is a library with no dev server of its own, so `e2e/harness/` is a
fixture-only vite app that mounts the components with no backend. A vite alias
pins the permission gate to "can edit" (mirroring the vitest setup) so dialog
submit buttons render enabled without AuthContext. The harness is excluded from
the published package (`package.json` `files` whitelists only `.tsbuild/` +
`assets/`) and from `tsc`/`eslint` (both scoped to `src` + `contracts`).

## Run it

```sh
# One-time: install the browsers (and system libs). Needs sudo for --with-deps.
npx playwright install --with-deps

# Run all three desktop engines against the harness (auto-starts the vite dev server):
npm run test:browsers

# One engine:
npm run test:browsers -- --project=chromium

# Run the mobile/tablet viewport suite (iPhone 14 / Pixel 7 / iPad gen 7):
npm run test:mobile

# One device:
npm run test:mobile -- --project=mobile-webkit
```
