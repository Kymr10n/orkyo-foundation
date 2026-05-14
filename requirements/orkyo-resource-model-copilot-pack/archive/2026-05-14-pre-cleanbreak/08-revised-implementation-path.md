# Revised Implementation Path

This supersedes the phase plan in doc 03 where the two conflict.
It is grounded in the findings of doc 07.

The headline change: **we generalize the existing schema in place
(rename + extend) rather than build a parallel resource model.**

## Guiding choices (taken as decided here — flag any objection)

- **Rename, don't duplicate.** `criteria` → `capabilities`,
  `space_capabilities` → `resource_capabilities`,
  `space_groups` → `resource_groups`. New tables: `resource_types`,
  `resources`, `capability_resource_types`. Old space tables stay.
- **Off-times Path B.** Generalize `off_time_spaces` →
  `off_time_resources`; absences ride on existing off_times.
- **Leaf-only assignments.** Only `planning_mode = 'leaf'` requests
  own `resource_assignments` rows. Summaries roll up read-only.
- **Feature flags via `tenant_settings`** under a new
  `scheduling.resource_model.*` descriptor namespace.
- **Typed capability matching kept.** The matcher must understand
  Boolean / Number (with operator) / String / Enum, not just presence.
- **Doc home:** `orkyo-foundation/docs/resource-model/`.

## Phase ordering

Each phase is independently deployable and independently revertible.
Migration numbering picks up at `1300.foundation.*`.

### Phase 0 — Inventory & safety net (no behavior change)

- Add `docs/resource-model/current-space-dependencies.md` enumerating:
  every file that references `space_id` / `SpaceId` / criteria / off_times,
  every endpoint, every test class, every frontend hook.
- Lock in the existing AutoSchedule scenario tests as the golden
  baseline; no edits to those tests until Phase 5.
- No code change.

### Phase 1 — Resource type + Resource tables (additive)

Migrations `1300.foundation.resource_types.sql`,
`1310.foundation.resources.sql`.

- Seed `resource_types`: `space`, `person`, `tool` (all `is_system=true`).
- `resources` table per doc 02 §`resources`, in `public` schema.
- DTOs + read-only `GET /api/resource-types` and `GET /api/resources`.
- No writes from product flows yet.

### Phase 2 — Map every Space to a Resource (additive + backfill)

Migration `1320.foundation.spaces_resource_link.sql`:

- `ALTER TABLE spaces ADD COLUMN resource_id uuid UNIQUE`.
- Backfill: for each space, insert a `resources` row of type `space`
  with `allocation_mode='Exclusive'`, `base_availability_percent=100`,
  and link via `spaces.resource_id`.
- Idempotent: re-running creates no duplicates.
- Triggers / app code: `SpaceService.Create` also creates the linked
  Resource; `SpaceService.Update(name)` mirrors to Resource.
- New endpoint `/api/spaces/{id}/resource` returns the link.

### Phase 3 — Capability / Group rename (in-place, reversible per migration)

Migration `1330.foundation.capability_rename.sql`:

- `ALTER TABLE criteria RENAME TO capabilities`.
- `ALTER TABLE space_capabilities RENAME TO resource_capabilities`,
  `RENAME COLUMN space_id TO resource_id`,
  add FK to `resources(id)`, drop FK to `spaces(id)` — backfill
  `resource_id` from `spaces.resource_id`.
- `ALTER TABLE space_groups RENAME TO resource_groups`,
  add `resource_type_id` (nullable; backfill to the `space` type id).
- `ALTER TABLE group_capabilities RENAME TO resource_group_capabilities`,
  `RENAME COLUMN group_id TO resource_group_id`.
- New table `capability_resource_types`; backfill every existing
  capability as applicable to `space`.
- Code: `CriteriaService` → `CapabilityService` (alias old paths
  via thin shim endpoints for one release).

This is the riskiest single phase. It must ship with:

- A pre-flight verification migration that asserts row counts pre/post.
- A revert migration committed at the same time.
- Frontend contracts kept stable: `criteria` API responses keep their
  shape; only the underlying table is renamed.

### Phase 4 — ResourceAssignment dual-write (additive)

Migration `1340.foundation.resource_assignments.sql`:

- Table per doc 02 §`resource_assignments`.
- `assignment_status` seeded with `Planned`, `Confirmed`, `Tentative`,
  `Cancelled` as a CHECK.
- App: every code path that sets/clears `requests.space_id` also
  writes/cancels a `resource_assignment` for the mapped resource.
  Only triggered on `planning_mode='leaf'` requests.
- Consistency check job (offline script under `scripts/`) that diffs
  `space_id` ↔ active assignment for every leaf request.
- Feature flag in `tenant_settings`:
  `scheduling.resource_model.dual_write_enabled` (default `true` once
  the dual-writer is shipped).

### Phase 5 — Off-times generalization + scheduler read switch

Migration `1350.foundation.off_time_resources.sql`:

- `CREATE TABLE off_time_resources (off_time_id, resource_id)`.
- Backfill from `off_time_spaces` joined to `spaces.resource_id`.
- Drop `off_time_spaces` only after one full release cycle of dual-read.

Scheduler:

- `SchedulingEngine` and `SchedulingRepository` switch to read
  resource_assignments + off_time_resources + resource absences via the
  resource id derived from `spaces.resource_id` for legacy callers.
- `AutoSchedule.SchedulingProblemBuilder` gains an
  `IResourceRequirement` seam — single-Space requirement is the only
  implementation. **OR-Tools / Greedy code is unchanged in behavior**;
  it just reads through the new abstraction.
- Feature flag: `scheduling.resource_model.reads_resource_assignments`.

The existing 130 scheduling/capability tests must pass unmodified.

### Phase 6 — Deprecate canonical `requests.space_id`

- `requests.space_id` becomes synchronized from the canonical
  ResourceAssignment write path; reads via a view.
- Marked deprecated in `docs/resource-model/`; not removed.

### Phase 7 — Person & Tool activation

- Admin CRUD endpoints for `resources` where
  `resource_type_id ∈ (person, tool)`.
- `AllocationMode.Fractional` enforced for person assignments,
  `Exclusive` default for tool.
- Frontend: new Resources admin area; Space UI untouched.
- Solver gains a second `IResourceRequirement` implementation —
  only if a request opts in.

### Phase 8 — Utilization

- Read-only endpoints, computed from assignments + off_times + base
  availability. No new storage of utilization values.

## Out of scope for this initiative (re-stating)

- Custom user-defined resource types.
- EF/ORM migration.
- Optimization solver overhaul beyond the `IResourceRequirement` seam.
- Any SaaS-only or Community-only resource code.
- BFF/auth/session changes.

## PR sequencing (one PR per phase, in order)

1. `phase-0/resource-model-inventory` — docs only.
2. `phase-1/resource-types-and-resources` — additive schema + read APIs.
3. `phase-2/space-resource-mapping` — backfill + write hook.
4. `phase-3/capability-rename` — rename in place + applicability table.
5. `phase-4/resource-assignment-dual-write` — additive + flag.
6. `phase-5/off-times-generalization-and-scheduler-read` — generalize + read switch.
7. `phase-6/space-id-deprecation` — view + docs.
8. `phase-7/person-tool-activation`.
9. `phase-8/utilization-reporting`.

Each PR ships its own migration revert script and updates
`docs/resource-model/` with the as-built notes.

## Gates that must pass before each PR merges

- All existing foundation tests green (no edits unless the phase's
  migration intentionally renames a column visible to tests).
- New tests for the phase's acceptance criteria from doc 06.
- Migration applied to a copy of staging data with row-count assertions.
- No new `${VAR:-default}` patterns introduced (foundation rule).
- No changes outside `orkyo-foundation` except contract bumps.

## Open questions for human approval before Phase 1

1. **Rename vs. parallel** — confirm the rename path (recommended) is
   acceptable. Alternative is parallel build, which doubles maintenance
   for ~6 months.
2. **Path B for off_times** — confirm generalizing `off_time_spaces` is
   acceptable, vs. keeping it Space-only forever.
3. **Leaf-only assignment scope** — confirm summary/container requests
   don't own assignments.
4. **Frontend stability window** — confirm Phases 1–5 ship with zero
   user-visible UI changes.
5. **Capability matching typing** — confirm numeric/enum matching is
   in-scope (yes, per current criteria behavior).

Until items 1–5 are answered, this revised path is a proposal, not a
plan.
