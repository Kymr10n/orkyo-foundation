# Orkyo Resource Model — Requirements Pack

Authoritative requirements for generalizing the Orkyo scheduling engine
from Space-allocation to Resource-allocation. Pack is intended to be
implemented by an autonomous coding agent (Claude Sonnet) one phase at
a time.

> **Status:** approved 2026-05-14. The `archive/` subdirectory contains
> superseded requirements; do not implement from those.
>
> **Live status of the initiative is tracked in [`STATUS.md`](STATUS.md).**
> The implementer updates that file as part of every phase PR. Read it
> first to learn the current state of the world.

## What is changing

The current foundation schedules requests against `spaces`. We are
generalizing the model so the scheduler operates on a `resources`
abstraction. A Space becomes one *type* of Resource (alongside Person
and Tool, with future types pluggable). The end state has:

- A `resources` table that is the schedulable identity.
- A `spaces` table that is a subtype-extension of `resources`
  (shared uuid; `spaces.id = resources.id`).
- `resource_assignments` as the single source of truth for which
  resources a request consumes. `requests.space_id` is removed.
- All `*_space*` tables and columns renamed to their resource
  equivalents.
- `criteria` keeps its name; it acts as a Capability when attached
  to a Resource and as a Requirement when attached to a Request.
  A new `criterion_resource_types` table tags which resource types
  each criterion is valid for, and a new `applicable_to_requests`
  column controls whether it can be a Request requirement.

## Guardrails

- **Clean break, no legacy retention.** After cutover (Phase 2),
  there are no `spaceId` synonyms, no `space_id` columns, no
  compatibility aliases, no feature flags gating read/write paths.
- **Build the new model in parallel first.** Phase 1 adds the new
  schema and a fully unit-tested service layer alongside Space code
  *without modifying any existing code path*. Phase 2 is the atomic
  cutover.
- **Backend and frontend rename in lockstep** at the rename PRs. No
  JSON aliases.
- **No new feature flags** for read/write path selection. The only
  acceptable flag use is admin-visibility for new Person/Tool screens.
- **Existing solver bodies (OR-Tools, Greedy) are not rewritten** —
  they are extended through an `IResourceRequirement` seam that keeps
  single-Space requests byte-equivalent.
- **Foundation only.** No SaaS or Community changes required by this
  initiative.

## Reading order

0. `STATUS.md` — live status of the initiative (read this first).
1. `01-resource-domain-model.md` — concepts, terms, invariants.
2. `02-database-schema.md` — final target schema with exact SQL.
3. `03-implementation-phases.md` — phase overview + sequencing.
4. `04-phase-1-parallel-build.md` — implementable spec for Phase 1.
5. `05-phase-2-cutover.md` — implementable spec for Phase 2.
6. `06-phase-3-applicability.md`
7. `07-phase-4-person-tool.md`
8. `08-phase-5-utilization.md`
9. `09-acceptance-criteria.md` — per-phase observable acceptance.
10. `10-implementation-rules.md` — DRY/KISS, no-flags, no-aliases,
    lockstep renames, test discipline.

## Phase summary

| Phase | Scope                                             | Risk |
|-------|---------------------------------------------------|------|
| 0     | Inventory + docs (`docs/resource-model/`)         | none |
| 1     | New tables, repos, services, endpoints (additive) | low  |
| 2     | Cutover: rename, drop space_id, rewire scheduler  | high |
| 3     | Criterion applicability tagging                   | low  |
| 4     | Person + Tool activation                          | medium |
| 5     | Utilization endpoints                             | low  |

Each phase is one PR. Phase 2 is the only large-blast-radius PR; it
is reviewable only as a whole because no half-renamed state is operable.

## Where supporting docs live

- Phase 0 inventory: `orkyo-foundation/docs/resource-model/current-space-dependencies.md`.
- Live initiative status + per-phase notes: `STATUS.md` in this pack.
- Migrations: `backend/migrations-foundation/sql/tenant/13xx.*.sql`.
- Code: `backend/src/` (Models, Services, Repositories, Endpoints).
- Frontend contracts: `frontend/contracts/`.
- Tests: `backend/tests/` and `frontend/src/**/__tests__/`.

## Out of scope

- Custom user-defined resource types (the model permits it; UI does not).
- EF/ORM introduction (current code is Dapper + raw SQL).
- BFF/session/auth changes.
- Optimization solver overhaul beyond the `IResourceRequirement` seam.
- SaaS- or Community-specific resource code.
