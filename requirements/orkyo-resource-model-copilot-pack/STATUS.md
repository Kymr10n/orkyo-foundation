# Resource Model Initiative — Status

Last updated: 2026-05-14 by planning session

## Phase board

| Phase | Title                       | State        | PR  | Migration | Updated     |
|-------|-----------------------------|--------------|-----|-----------|-------------|
| 0     | Inventory                   | Not started  | —   | n/a       | 2026-05-14  |
| 1     | Parallel build              | Not started  | —   | 1300      | 2026-05-14  |
| 2     | Cutover                     | Not started  | —   | 1310      | 2026-05-14  |
| 3     | Criterion applicability     | Not started  | —   | 1320      | 2026-05-14  |
| 4     | Person + Tool activation    | Not started  | —   | 1330      | 2026-05-14  |
| 5     | Utilization                 | Not started  | —   | 1340      | 2026-05-14  |

State vocabulary (fixed set):
`Not started` | `In progress` | `In review` | `Merged` | `Blocked`

## Update rules

- The implementer MUST update this file as part of every phase PR.
- Updating STATUS.md is the **last** step of every phase, before
  opening the PR.
- The PR description must include a diff link to the STATUS.md change.
- State transitions:
  - `Not started` → `In progress` when work begins.
  - `In progress` → `In review` when the PR is opened.
  - `In review` → `Merged` when the PR is merged.
  - Any state → `Blocked` if a decision is needed from the user.
- When transitioning to `Blocked`, add an entry to "Open issues /
  deviations" describing what's blocked and what decision is needed.

## Phase log

One section per phase. Filled in by the implementer when the phase
begins, updated when it ships. This replaces the previously-planned
per-phase `docs/resource-model/phase-N-as-built.md` files.

### Phase 0 — Inventory

_Not started._

### Phase 1 — Parallel build

_Not started. Spec: `04-phase-1-parallel-build.md`._

### Phase 2 — Cutover

_Not started. Spec: `05-phase-2-cutover.md`._

### Phase 3 — Criterion applicability

_Not started. Spec: `06-phase-3-applicability.md`._

### Phase 4 — Person + Tool activation

_Not started. Spec: `07-phase-4-person-tool.md`._

### Phase 5 — Utilization

_Not started. Spec: `08-phase-5-utilization.md`._

## Open issues / deviations

_None yet._

## Decisions log

- **2026-05-14** — Clean break (no `spaceId` retention, no compat
  shims, no read/write feature flags) approved. Rationale: foundation
  is itself a carve-out; pay the migration cost once.
- **2026-05-14** — Parallel build (Phase 1) before atomic cutover
  (Phase 2). Rationale: derisk the cutover by proving the new model
  works standalone first.
- **2026-05-14** — Shared-uuid Space-as-Resource (Phase 2):
  `spaces.id = resources.id`. Rationale: cleanest expression of
  "Space is a Resource"; avoids id remapping across 393 frontend
  references.
- **2026-05-14** — `criteria` table stays as `criteria`; applicability
  tagging added. Rationale: existing schema already supports criteria
  as both Capability and Requirement; rename would be churn for no
  gain.
- **2026-05-14** — Single STATUS.md replaces per-phase
  `phase-N-as-built.md` files. Rationale: single source of truth for
  initiative state.
