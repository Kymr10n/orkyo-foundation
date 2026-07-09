# Cross-engine smoke suite (measure M7)

A minimal Playwright suite that renders a slice of real foundation components
(FormDialog, OrkyoDataTable, TimelineGridShell + off-time hatch, RequestCalendar,
ScheduleToDialog) against fixture data, across **chromium / webkit / firefox**.
Its job is narrow: catch parse errors from too-new syntax and gross cross-engine
render breakage. Component *logic* is covered by the vitest suite.

## Not a CI gate

This is **on-demand only** and deliberately **not wired into CI**. Grow it only
when a real cross-engine bug surfaces — one spec file, one Playwright config.

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

# Run all three engines against the harness (auto-starts the vite dev server):
npm run test:browsers

# One engine:
npm run test:browsers -- --project=chromium
```
