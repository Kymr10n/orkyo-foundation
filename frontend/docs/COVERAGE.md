# Frontend Test Coverage

**Target:** ≥ 80% statement/line coverage per file, and in aggregate. New code should
ship with tests; PRs that drop a file below 80% should either add tests or record an
explicit exception here.

## Current aggregate (vitest + V8)

| Metric | % |
|---|---|
| Statements | ~83.9 |
| Lines | ~85.7 |
| Functions | ~81.5 |
| Branches | ~76.8 |

~85% of source files (234 / 275) are at or above 80% statements. Run it yourself:

```
cd frontend && npx vitest run --coverage
```

Statements, lines, and functions clear the 80% bar in aggregate. **Branch coverage
(~76.8%) is the one aggregate metric still below 80%** — it's dominated by defensive
branches (early returns, `?? fallback`, optional props) inside the large interaction
components listed under exceptions below. Chipping those down is the main remaining work.

## Documented exceptions

These files sit below 80% by category, with the reason they aren't unit-tested here.
"Exception" means *not cost-effective to unit-test in this repo's `happy-dom` env or
this layer* — not "untested": most are exercised by product integration tests and/or
manual QA.

### 1. Thin API clients — covered by product integration tests
`lib/api/*-api.ts` (e.g. `resource-assignments-api`, `person-candidate-requests-api`,
`resource-utilization-api`, `quotas-api`, `admin-api`, `utilization-api`).

These are passthrough wrappers over `core/api-client` (`apiGet/apiPost/...`) — URL +
type plumbing, no logic. Per `CLAUDE.md`, *"integration tests against product wiring
stay in the product repo"*; saas/community exercise these against the real BFF. Unit
tests here would only assert the literal URL string. **Reasonable exception.**

### 2. Drag-and-drop & timeline grids — interaction needs a real layout engine
`components/utilization/SchedulerGrid`, `PeopleUtilizationGrid`, `CollapsibleFloorplan`,
`components/spaces/SpaceManagementPanel`, and the dnd/keyboard/context-menu paths of
`components/requests/RequestTreeView`.

`@dnd-kit` pointer/keyboard sensors and the virtualized timeline rely on real geometry
(`getBoundingClientRect`, pointer capture, scroll position) that `happy-dom` doesn't
compute, so drag interactions can't be meaningfully simulated. Render/empty/loading
paths are covered; the drag choreography is verified by manual QA. **Partial exception.**

### 3. Browser telemetry
`lib/core/rum.ts` — wraps `web-vitals` / `PerformanceObserver`, which are browser-only
and side-effecting. Not unit-testable in `happy-dom`; validated in the browser.

### 4. Thin composition / data hooks
`hooks/useQuotas`, `hooks/useConflictRegistry`, `hooks/useRequestForm` — small
React-Query / state-composition wrappers, exercised through the components that consume
them (e.g. `UsageLimitsSettings` mocks `useQuotas`). Testing them in isolation mostly
re-asserts React Query.

### 5. Large feature dialogs/pages — improvement opportunities (not hard exceptions)
`components/settings/AvailabilityEventDialog`, `pages/RequestsPage`, `pages/UtilizationPage`,
`pages/OnboardingPage`, and several settings editors (`OrganizationSettings`,
`UserSettings`, `TemplateSettings`, `CriteriaSettings`, …).

These *are* unit-testable; they're below 80% because deep form-interaction branches
weren't covered in this pass. Their render/happy paths are covered. These are the best
next targets to lift branch coverage — **track as work, not permanent exceptions.**

## What the UX-consistency branch added

Coverage work accompanying the scroll/dialog changes:
- New primitive tests: `ScrollArea.viewportRef`, `ScrollableDialogBody`, `FormDialog`
  (was entirely untested), `LoadingSpinner` a11y, and a `jest-axe` smoke harness.
- Pure logic raised to ~90–100%: `lib/utils/utils` (status/format/payload helpers),
  `lib/quotas/quota-display` (`formatBytes`), `components/utilization/time-grid-offtime`.
- UI primitives 0/38% → ~96%: `components/ui/combobox`, `components/ui/date-time-picker`
  (enabled by stubbing `Element.prototype.scrollIntoView` for Radix Select under happy-dom).
- State/feature: `store/request-tree-store` (collapse + hydration branches),
  `components/settings/StorageUsageMonitor` (0→100%), `UsageLimitsSettings` (0→~86%).
