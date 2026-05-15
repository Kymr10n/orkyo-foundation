# Current State Analysis vs. the Pack

This document captures findings from reading the actual `orkyo-foundation` code
before the resource-model refactor starts. It does **not** rewrite docs 01–06;
it lists discrepancies, hidden costs, and decisions that must be made before
Phase 1.

Authored 2026-05-14 against `orkyo-foundation` main.

## 1. Existing primitives the pack ignores or duplicates

| Pack concept                | Existing primitive                                                                 | Decision required                                                              |
|-----------------------------|------------------------------------------------------------------------------------|--------------------------------------------------------------------------------|
| `capabilities`              | `criteria` (typed: Boolean/Number/String/Enum, with `unit`, `enum_values`)         | Rename `criteria` → `capabilities`, **or** keep `criteria` as the definition and treat `capability` as alias. Recommendation below. |
| `resource_capabilities`     | `space_capabilities (space_id, criterion_id, value jsonb)`                         | Generalize to `resource_capabilities (resource_id, capability_id, value jsonb)`. **Preserve the typed `value`.** Pack treats Capability as flag-like — that is a regression. |
| `capability_resource_types` | (none — criteria are implicitly Space-only today)                                  | Add as new table; backfill all existing criteria as applicable to `space`.     |
| `resource_groups`           | `space_groups` (+ `spaces.group_id`, `group_capabilities`)                         | Generalize `space_groups` → `resource_groups`. Pack should not introduce a parallel grouping concept. |
| `resource_group_capabilities` | `group_capabilities (group_id, criterion_id, value jsonb)`                       | Rename targets when groups generalize.                                         |
| `resource_absences`         | `off_times` + `off_time_spaces` (site-scoped, per-space exclusions, recurring)     | Two overlapping concepts. Choose one path (see §4).                            |
| `resource_assignments`      | `requests.space_id` (single nullable FK)                                           | Pack's plan stands; dual-write is correct.                                     |

**Recommendation:** rename rather than parallel-build. The current schema is
already a resource model in everything but name; introducing a parallel
`capabilities` / `resource_capabilities` set alongside `criteria` /
`space_capabilities` would create exactly the "duplicate resource logic"
the pack's own Architecture Rules forbid.

## 2. Typed capability values

The pack's `Capability` is flag-style (`key`, `display_name`, …) with optional
`value` on the assignment. The existing model is stronger:

- `criteria.data_type` ∈ {Boolean, Number, String, Enum}
- `criteria.unit` (e.g. `kg`, `m`)
- `criteria.enum_values jsonb`
- `space_capabilities.value jsonb` typed by the criterion

This matters for matching rules like *"forklift with lift capacity ≥ 500 kg"*
or *"room with capacity ≥ N"*. Doc 04 (Scheduler Integration) does not
specify numeric/enum matching semantics — only flag presence. **Add typed
matching to the capability matcher contract** before Phase 3.

## 3. Hierarchical requests

`requests.parent_request_id` + `planning_mode ∈ (leaf, summary, container)`
exist today. The pack does not say:

- Whether non-leaf requests can hold `ResourceAssignment`s.
- Whether a summary request rolls up child assignments for utilization.
- Whether allocation-mode rules apply per leaf or per tree.

**Decision needed before Phase 4.** Default proposal: only `leaf` requests
own `ResourceAssignment` rows; summary/container roll up read-only.

## 4. off_times vs. resource_absences

Two viable paths — must be chosen explicitly:

- **Path A — keep `off_times` (site-scoped) + add `resource_absences` (resource-scoped).**
  Scheduler unions both. Simpler migration, but two tables to query.
  `off_time_spaces` stays Space-only and is **not** generalized.

- **Path B — generalize `off_time_spaces` → `off_time_resources`, keep one absence concept.**
  Site-level off-times remain; per-resource exclusions ride on the existing
  table. Drop `resource_absences` as a separate table.

**Recommendation: Path B.** It avoids two parallel "unavailable window"
concepts and reuses the recurring-rule logic already in `off_times`.
Personal absences (vacation, sick) become per-resource off_times with an
`absence_type`.

## 5. Scheduler integration with existing settings

`requests.scheduling_settings_apply` (bool) gates whether site working-hours/
off-times apply to a specific request. The pack's scheduler rules don't say
how this flag interacts with `resource_absences` / `AllocationMode`.
**Spec needed.** Reasonable default: the flag continues to gate site-level
constraints only; resource absences and allocation always apply.

## 6. Auto-scheduling (OR-Tools + Greedy)

`backend/src/Services/AutoSchedule/` has:

- `SchedulingProblemBuilder` (96 LOC)
- `OrToolsSchedulingSolver` (225 LOC)
- `GreedySchedulingSolver` (117 LOC)
- `SchedulingFeasibilityAnalyzer` (135 LOC)

All assume a single Space per Request. The pack defers multi-resource solving
to "later" but does not specify the seam. **Before Phase 5 read-switch:**
introduce an `IResourceRequirement` abstraction in the problem builder so
the solver continues to see "one required resource of type Space" without
behavioral change.

## 7. Templates, presets, starter templates

`templates`, `request_templates`, `presets`, `StarterTemplateService` all
reference Spaces. Pack doesn't list these. They must continue to work
across the migration — preset application should resolve Space → Resource
via the mapping introduced in Phase 2.

## 8. Where doc artifacts live

Pack references `docs/resource-model/current-space-dependencies.md` without
a repo. **Place all migration artifacts under `orkyo-foundation/docs/resource-model/`.**

## 9. Feature flags

Pack lists flags (`ResourceModelEnabled`, `SchedulerReadsResourceAssignments`, …)
without a mechanism. Foundation already has `tenant_settings` (typed,
descriptor-driven via `TenantSettingDescriptorCatalog`). Use it. Add a
new descriptor category `scheduling.resource_model.*` rather than a new
config system.

## 10. Tenant DB schema

All existing tenant tables live in `public`. The pack does not specify a
schema; new tables go into `public` as well.

## 11. Frontend impact (under-specified)

Frontend has `SpaceEndpoints`, `SpaceCapabilityEndpoints`,
`GroupCapabilityEndpoints`, `CriteriaEndpoints` and matching React surfaces.
Pack says "UI stays Space-centric in early phases" but doesn't enumerate.
**Add a frontend impact list** to the Phase 0 deliverable, covering:

- Space list / detail / capability editor
- Group capability editor
- Criterion (→ Capability) admin
- Request edit (Space picker)
- Scheduler/calendar views

For Phases 1–5, none of these should change behaviorally; they read through
adapters where needed.

## 12. Test surface

`backend/tests/` has 232 test files; ~130 touch space/scheduling/capability/
criterion/request. Phase 0 must add a "golden path" regression baseline
before any rename. The existing AutoSchedule scenario tests
(`SchedulingScenarioTests`, `OrToolsSchedulingSolverTests`,
`GreedySchedulingSolverTests`) are the highest-value safety net — they
should pass unchanged through Phase 5.

## 13. SaaS / Community right now

Neither `orkyo-saas/backend/src` nor `orkyo-community/backend/src` re-implements
resource logic today; they consume foundation. Pack's worry about
"don't duplicate between SaaS and Community" is not a present-day risk —
it's a constraint to maintain. No SaaS-specific resource code needs to be
written in this initiative.

## 14. Non-goals not yet in the pack

Add explicitly to the non-goals list:

- No custom UI for resource type management.
- No introduction of an EF/ORM layer (current code is Dapper + raw SQL).
- No change to migration runner (`Orkyo.Foundation.Migrations`).
- No new schema (`public` only).
- No change to BFF session / auth model.

## Summary of decisions needed before Phase 1 starts

1. Rename existing tables (Path: criteria→capabilities, space_capabilities→
   resource_capabilities, space_groups→resource_groups) **or** layer new
   tables in parallel.
2. Choose off_times Path A vs. Path B.
3. Specify ResourceAssignment behavior for non-leaf requests.
4. Confirm Path B for tenant_settings as feature-flag carrier.
5. Confirm typed capability matching in scope.
6. Confirm `orkyo-foundation/docs/resource-model/` as doc home.

Until these are answered, Phase 1 should not begin. None of them are
expensive to decide; all of them are expensive to retrofit.
