# Implementation Rules

Rules an autonomous implementer (Claude Sonnet) must follow when
producing each phase PR. Violations are grounds for review rejection.

## Architectural rules

1. **Foundation-only.** All work happens in
   `/home/alex/Projects/orkyo-foundation`. No edits to
   `orkyo-saas`, `orkyo-community`, or `orkyo-core` (the last is
   read-only reference).
2. **No backwards-compatibility hacks.** No `spaceId` synonyms after
   Phase 2. No JSON aliases. No deprecated-but-kept columns past their
   phase. If a field is renamed, it is renamed everywhere in lockstep.
3. **No feature flags gating read/write paths.** The only acceptable
   flag use in this initiative is an admin-visibility toggle for the
   Phase 4 Person/Tool screens (and even that is optional).
4. **One PR per phase.** No phase is partially shipped. No phase is
   bundled with another. Phase 2 is the only large PR; it is one PR
   by necessity because no half-renamed state is reviewable.
5. **No new abstractions beyond what the spec lists.** Don't add a
   repository pattern layer, an event bus, a CQRS split, or
   speculative interfaces. `IResourceRequirement` is the only new
   abstraction this initiative introduces and it is mandated.
6. **No new dependencies.** No new NuGet or npm packages without
   explicit approval.

## Code quality rules

7. **DRY at the helper level, not at the architecture level.** Extract
   shared logic into helpers (e.g. `OffTimeFilter.AppliesTo(...)`)
   when two call sites need it. Don't extract speculative bases.
8. **No string literals for domain keys.** Use the constants files
   defined in Phase 1 (`ResourceTypeKeys`, `AllocationModes`,
   `AssignmentStatuses`).
9. **No silent fallbacks.** Validation failures return structured
   `ResourceConflict` objects (services) or 4xx with structured body
   (endpoints). Never swallow.
10. **No `${VAR:-default}` patterns.** Foundation rule: defaults live
    in `.env.template`. Bare `${VAR}` only.
11. **Default to no comments.** Code reads itself. The exceptions are
    non-obvious *why* notes (hidden constraints, surprising
    invariants, subtle SQL behavior).

## Migration rules

12. **Migrations are idempotent where the schema permits.** Backfills
    use `ON CONFLICT DO NOTHING`. Renames are not idempotent — that's
    acceptable because each phase runs once.
13. **Every migration ships with a revert script** in
    `backend/migrations-foundation/sql/tenant/revert/`.
14. **Data-parity gates are enforced at the migration level**, not
    afterwards. Phase 2 migration's `DO $$ … RAISE EXCEPTION` block
    is mandatory.
15. **Migrations are tested on a clone of staging data** before merge.
    Pre/post row-count comparisons are part of the PR description.
16. **Migration numbering is strictly sequential.** Phase 1 = 1300,
    Phase 2 = 1310, Phase 3 = 1320, Phase 4 = 1330, Phase 5 = 1340.
    If a phase needs more than one migration, use 1301, 1302, etc.

## Testing rules

17. **Regression baseline tests are sacred.** Their assertions never
    weaken. Only identifier/property renames are permitted, and only
    at the phase that performs the rename. Spec the rename in the PR
    description.
18. **Every new service method ships with a unit test.** Every new
    endpoint ships with at least a happy-path test and an
    authorization test.
19. **No mocking of the database in scheduling tests.** Hit Postgres.
    The existing test infrastructure (`backend/tests/Integration/`)
    is the pattern.
20. **Test names describe behavior, not implementation.**
    `FractionalResource_AssignmentExceedingBaseAvailability_Rejected`,
    not `Test123` or `WhenAllocPctTooHigh_ReturnsFalse`.

## API + frontend rules

21. **Backend and frontend rename in lockstep** in the same PR. There
    is no separate "backend ships, frontend follows" flow for renames.
22. **Endpoints follow REST conventions** and the patterns established
    in existing foundation endpoints (`SiteEndpoints`,
    `SpaceEndpoints`). Pluralize collection routes
    (`/api/resources`), use kebab-case in paths
    (`/api/resource-types`).
23. **DTO fields are camelCase in JSON** (already the project default
    via System.Text.Json conventions). C# properties are PascalCase.
24. **Authorization: admin-only for resource/capability/group
    management.** Reuse the existing admin policy. Do not invent new
    auth policies.

## Communication rules

25. **Each phase PR description includes:**
    - Link to the phase spec doc.
    - Summary of what migration applied + row-count diffs from staging.
    - List of files touched + reason (one line each).
    - The `grep` guardrail output where applicable.
    - Diff link to the `STATUS.md` update made by this PR.
26. **STATUS.md is the single progress tracker** for this initiative.
    Every phase PR updates `STATUS.md`: the phase board row state, the
    phase log section, and (when applicable) the decisions log. There
    are no per-phase as-built files; the Phase log section of
    `STATUS.md` replaces them. Updating `STATUS.md` is the **last**
    step of every phase, before opening the PR.
27. **When in doubt, ask.** Plan-mode questions are cheap. Surprise
    architectural decisions are expensive. The user has explicit
    feedback memory rules against unilateral architecture changes.

## Out-of-scope reminders

These are not part of any phase:

- Custom user-defined resource types (UI).
- ORM/EF introduction.
- BFF / session / auth changes.
- Optimization solver rewrite (only the `IResourceRequirement` seam).
- SaaS- or Community-specific code.
- New dependencies.
- Coverage gates or CI thresholds.

If a phase appears to require any of the above to be implementable,
stop and ask the user.
