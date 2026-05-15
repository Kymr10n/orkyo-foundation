# People Resources — Implementation Status

**Single source of truth for progress on this initiative.**
The implementing agent updates this file at the end of every phase and at any
meaningful checkpoint (blocker found, scope change, partial PR merged).

> Rules for the implementing agent:
> 1. Read this file before starting any work in a session.
> 2. After finishing a phase, flip its checkbox to ✅, fill in the date, the
>    commit / PR link, the test names added, and any deviations from
>    [02_people_resources_implementation_plan.md](02_people_resources_implementation_plan.md).
> 3. If a phase is paused or blocked, mark it 🟡 and write the blocker under
>    "Open issues / blockers".
> 4. If a decision in [01_people_resources_specification.md §18](01_people_resources_specification.md)
>    has to change, record it under "Decision changes" with rationale **before**
>    coding around it.
> 5. Never delete history from this file — append, don't rewrite.

---

## Summary

| Phase | Title | Status | Date | Commit / PR |
|------:|-------|:------:|------|-------------|
| 0 | Kickoff & baseline | ✅ | 2026-01-15 | 9405645 |
| 1 | Schema deltas | ✅ | 2026-01-15 | Migration 1400 + tests |
| 2 | Off-time + typed capability checks | ✅ | 2026-05-15 | Behaviour delivered via the validator created in Phase 3; CreateAsync delegates. See revised Phase 2 log. |
| 3 | Validation dry-run endpoint | ✅ | 2026-05-15 | Validator split into per-check methods; weekend + holiday checks added; ValidationIssue→ResourceConflict converter rewritten as dictionary (was brittle string-switch). See revised Phase 3 log. |
| 4 | Person profile API | ✅ | 2026-05-15 | Endpoints wired in foundation test factory, SaaS, and Community Program.cs. SQL syntax in `UnlinkUserAsync`/`LinkUserAsync`/`UpsertAsync` fixed (3× malformed `@$` literals); CITEXT email cast to text for Npgsql read; link auto-upserts profile; FluentValidator + input length limits added. 7 integration tests passing. |
| 5 | Frontend: People page | 🟡 | 2026-05-15 | People tab complete: `PersonEditDialog` saves both resource + person-profile APIs; 4 vitest tests; API client bugs fixed; `RequestAssignmentsSection.tsx` deleted (premature Phase 6 stub). Groups + Absences tabs remain placeholders — see Open issues. |
| 6 | Request dialog: People assignment | ⬜ | | |
| 7 | Utilization page: People grid | ⬜ | | |
| 8 | Polish, docs, release notes | ⬜ | | |

Legend: ⬜ not started · 🟡 in progress / blocked · ✅ done

---

## Phase logs

### Phase 0 — Kickoff & baseline
- Status: ✅
- Baseline commit: `94056457c931121c007eadbaab39b82924e250c6`
- Notes: Completed 2026-01-15. Baseline commit includes the Phase 6 Read-Model Unification implementation. All source and test files verified clean of `primaryResourceId`/`PrimaryResourceId` references.

### Phase 1 — Schema deltas
- Status: ✅
- Migration file: `1400.foundation.people.sql`
- Columns / tables added: `person_profiles` table, `resource_groups.default_availability_percent`, `resource_assignments.role`, `resource_assignments.notes`
- Tests added: `TestTenant_ShouldContain_PersonProfilesTable`, `TestTenant_ResourceGroups_ShouldHave_DefaultAvailabilityPercentColumn`, `TestTenant_ResourceAssignments_ShouldHave_RoleColumn`, `TestTenant_ResourceAssignments_ShouldHave_NotesColumn` in `MigrationHarnessSmokeTests.cs`
- Notes: Delivered 2026-05-15. Migration created with `-- @migration-class: schema-additive` header. Revert script created at `1400.foundation.people.revert.sql`. Tests validate schema exists after migration.

### Phase 2 — Off-time + typed capability checks
- Status: ✅ (revised 2026-05-15)
- Initial implementation (2026-01-15) injected `IOffTimeResourceQuery`, `IRequestRepository`, `ISchedulingRepository` directly into `ResourceAssignmentService`. **Phase 3 superseded this**: the same behaviours moved into `ResourceAssignmentValidator`, and `ResourceAssignmentService.CreateAsync` became a thin delegation layer. The Phase 2 "off-time check" and "typed capability check" tests in `ResourceAssignmentServiceTests.cs` were dropped on 2026-05-15 because they tested the obsolete inline shape; the validator tests in `ResourceAssignmentValidatorTests.cs` cover the same scenarios.
- Files final state:
  - `backend/src/Services/ResourceAssignmentService.cs`: 2-arg ctor (assignment repo + validator); delegates to validator and translates the first blocker into the legacy `ResourceConflict` shape via a `ValidationReasonCode→ResourceConflictType` dictionary.
- Behaviour delivered: off-time overlap raises warnings (holidays surface as a distinct `NonWorkingHoliday` code, others as `OffTimeOverlap`); typed capability checks via `ResourceSatisfiesRequirementAsync`.

### Phase 3 — Validation dry-run endpoint
- Status: ✅ (revised 2026-05-15)
- Endpoint: `POST /api/resource-assignments/validate`
- Reason codes implemented (all 10): `resource.not-found`, `resource.inactive`, `resource.type-mismatch`, `capability.missing`, `offtime.overlap`, `assignment.overbooked`, `nonworking.weekend`, `nonworking.holiday`, `allocation-mode.invalid`, `allocation-percent.invalid`
- Cleanup 2026-05-15:
  - 185-line `ValidateAsync` method split into per-check private methods (`CheckCapabilitiesAsync`, `CheckSiteScopedWindowAsync` → `CheckOffTimesAndHolidaysAsync` + `CheckWeekendsAsync`, `CheckAllocationAsync`). Orchestrator is one screen.
  - **Weekend and holiday checks implemented** (were stub `Assert.Equal(Ok)` placeholders). Weekends use `SchedulingEngine.IsWeekend` (made public) and respect `scheduling_settings.weekends_enabled`. Holidays are distinguished from generic off-times by `OffTimeType.Holiday` and surface as the dedicated `NonWorkingHoliday` reason code.
  - `ValidationReasonCode → ResourceConflictType` mapping in `ResourceAssignmentService` rewritten from brittle string-switch to a `Dictionary` with explicit `InvalidOperationException` on unmapped codes.
  - Mock-default returns added to `ResourceAssignmentValidatorTests` constructor (Moq returned `null` Tasks → NRE on iteration).
- Tests in `ResourceAssignmentValidatorTests.cs` (14 passing): all 10 reason codes covered. `NonWorkingWeekend_WeekendsEnabled_DoesNotWarn` added to prove the `weekends_enabled=true` negative path.

### Phase 4 — Person profile API
- Status: ✅ (revised 2026-05-15 — original 2026-01-15 impl was build-broken)
- Endpoints: `GET /api/person-profiles/{resourceId}`, `PUT /api/person-profiles/{resourceId}`, `POST /api/person-profiles/{resourceId}/link`, `DELETE /api/person-profiles/{resourceId}/link`
- Wiring: `MapPersonProfileEndpoints()` now called from `FoundationWebApplicationFactory` (tests), `orkyo-saas/backend/api/Program.cs`, and `orkyo-community/backend/api/Program.cs`. SaaS additionally dropped 3 dead references to the removed `SpaceCapability*` symbols (a Phase 2 resource-model carryover).
- Bugs fixed during cleanup:
  - **Build was broken**: 3× malformed `@$` SQL string literals in `PersonProfileRepository` (54 cascading compile errors). Fixed.
  - **CITEXT read**: Npgsql 8+ has no `CITEXT` handler; `SelectColumns` now casts `email::text AS email`.
  - **Link UX**: `LinkUserAsync` was a conditional UPDATE that failed when no profile row existed yet; rewritten as `INSERT … ON CONFLICT DO UPDATE` so admins can link without first manually saving an empty profile.
  - **Upsert SQL**: collapsed branched INSERT/UPDATE into a single `INSERT … ON CONFLICT DO UPDATE` round-trip (DRY); extracted `NullableParam` helper.
  - **Endpoint chain**: `.RequireAdminAccess()` was on the wrong builder type; moved to each write endpoint.
  - **Magic strings**: extracted `NotAPersonMessage` const + extracted the repeated "load resource + assert person" preamble into `ResolvePersonResourceAsync`.
  - **Input validation**: added `UpsertPersonProfileRequestValidator` (FluentValidation): email format, 254-char limit, 200-char limits on jobTitle/department.
- Tests in `PersonProfileEndpointsTests.cs` (7 passing, no longer `[Skip]`): coverage for not-found / not-a-person / upsert round-trip / link / 409 duplicate-link / unlink / unlink-not-linked. Tests use the existing `CreateTestUserAsync` helper so FK constraints are honoured.

### Phase 5 — Frontend: People page
- Status: 🟡 (revised 2026-05-15 — People tab ✅; Groups + Absences tabs remain placeholders)
- Route + nav wiring: Added `/people` route in TenantApp.tsx between `/spaces` and `/requests`; added `{ to: "/people", label: "People", icon: Users }` to navItems in SidebarNav.tsx
- Components added: `PeoplePage.tsx`, `components/people/` (PersonList, PersonEditDialog, PeopleGroupList, PeopleGroupEditDialog, PersonAbsenceList, PersonAbsenceEditDialog)
- Tests added: `frontend/src/components/people/PersonEditDialog.test.tsx` — 4 vitest tests: field rendering, create calls both APIs, edit calls both APIs, Save disabled until name filled
- API client bugs fixed (2026-05-15 cleanup):
  - `resources-api.ts`: `API_PATHS.resources` → `API_PATHS.RESOURCES` (2 occurrences; was causing 404 at runtime)
  - `person-profiles-api.ts`: `apiDelete<void>(...)` → `apiDelete(...)` (invalid generic removed)
  - `PersonAbsenceEditDialog.tsx`: deprecated `React.FormEvent` → `React.SyntheticEvent<HTMLFormElement>`; `initialFocus` → `autoFocus`
  - `PeopleGroupList.tsx`: removed broken import of `useGroupsApi` from wrong module; rendered as placeholder
  - `RequestAssignmentsSection.tsx`: **deleted** — premature Phase 6 work that imported a non-existent `resource-assignment-api` module
- Notes: `PersonEditDialog` now saves BOTH resource (name, allocationMode, baseAvailabilityPercent) AND person-profile (email, jobTitle, department, notes) in a single sequential mutation. Profile is loaded via `getPersonProfile` on open for edit mode. Groups and Absences tabs remain placeholder content — full implementation needs backend extension (see Open issues). API paths for resources and person-profiles are in `api-paths.ts`. Components use shadcn/ui for consistent styling.

### Phase 6 — Request dialog: People assignment
- Status: ⬜
- Files changed:
- Tests added:
- Notes:

### Phase 7 — Utilization page: People grid
- Status: ⬜
- Files changed:
- Tests added:
- Notes:

### Phase 8 — Polish, docs, release notes
- Status: ⬜
- Docs touched:
- Release notes line:

---

## Decision changes

Append entries here whenever a decision in 01 §18 has to be revised. Format:

```
- YYYY-MM-DD — <decision id / topic>
  Old: <what 01 §18 said>
  New: <what we are doing instead>
  Why: <rationale>
  Phase impact: <which phases need to be redone or amended>
```

(none yet)

---

## Open issues / blockers

Append entries here when a phase is paused. Remove the entry (and add a closing
note in the phase log) once it's resolved.

### 2026-05-15 — Phase 5 follow-up: Groups + Absences tabs

The Groups tab and Absences tab on `PeoplePage.tsx` are placeholders. Full
implementation needs backend extension that wasn't in the original Phase 5
scope:

- **Groups CRUD**: `/api/groups` (legacy `SpaceGroupEndpoints`) does not expose
  `default_availability_percent` (added by migration 1400) or filter by
  `resource_type_id`. Either extend `SpaceGroupRepository` + `CreateSpaceGroupRequest`
  to surface the new column, or add a `/api/resource-groups` CRUD that handles
  all resource types.
- **Absences tab**: per-person absences already work via `/api/resources/{id}/absences`.
  The tab needs to list off-times across multiple people in one view, which is
  a new query shape (no existing endpoint aggregates by resource type).

The People tab itself (the §18.5 acceptance criterion for "create/edit person
with email, job_title, department, notes, linked user") IS shipped in this
cleanup; see Phase 5 log.

### 2026-05-15 — SaaS Program.cs missing resource-model endpoints

Separate from People, the SaaS Program.cs is missing all `MapResource*` /
`MapCriterion*` / `MapUtilizationEndpoints` calls that Community already has.
The People feature CANNOT ship to SaaS until that wiring is added. Out of
scope for this initiative; flagged here so it surfaces during a SaaS-rollout
phase.

---

## Deferred items (tracked, not implemented in v1)

Mirrors 01 §18 / 02 "Deferred" — listed here so future sessions can find them:

- User invitation flow
- Group default capabilities
- Group-scoped absences
- Regional holiday provider
- Drag-and-drop assignments
- `ConcurrentCapacity` allocation mode
