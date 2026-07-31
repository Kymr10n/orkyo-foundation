# Findings on `main` — July 2026

Written while implementing user-definable resource types on
`claude/generic-resource-types-r9m82d`. Every item below was verified by reading the code at the
cited path.

**Sections 1–3 describe `main` as it stands and are not touched by that branch.** Section 4
records errors in the branch itself, for completeness.

---

## 1. Correctness / hygiene issues

### 1.1 `JsonDocument` instances are never disposed (low severity, several sites)

`JsonDocument.Parse` rents its backing buffer from `ArrayPool`. Not disposing does **not**
corrupt data or crash — the GC still reclaims it — but the buffer is never returned to the pool,
so these paths cause avoidable allocation churn on read paths.

| File | Line | Shape |
|---|---|---|
| `backend/core/Repositories/ResourceCapabilityRepository.cs` | 159 | `JsonDocument.Parse(valueJson).RootElement` — **no `.Clone()`** |
| `backend/core/Models/Preset/PresetValidator.cs` | 298 | `JsonDocument.Parse(item.Value);` — result discarded |
| `backend/core/Services/Preset/PresetApplier.cs` | 457 | `JsonDocument.Parse(value);` — result discarded |
| `backend/core/Repositories/TemplateRepository.cs` | 212 | `try { JsonDocument.Parse(item.Value); }` — result discarded |
| `backend/core/Repositories/UserPreferencesRepository.cs` | 17 | returns an undisposed `JsonDocument` to `UserPreferencesEndpoints.cs:22` |

**`ResourceCapabilityRepository.cs:159` is the one worth fixing first.** It is the only site that
hands a `JsonElement` out of a parsed document without cloning, so the element keeps the whole
`JsonDocument` alive for the lifetime of the DTO. Compare `backend/core/Repositories/RequestMapper.cs`
(lines 54, 56, 70, 73), which already does the right thing:

```csharp
Value = JsonDocument.Parse(reader.GetString("value")).RootElement.Clone(),
```

The fully-correct form — returns the buffer *and* detaches the element — is:

```csharp
using var doc = JsonDocument.Parse(valueJson);
return doc.RootElement.Clone();
```

For the three fire-and-forget validity checks, `using var _ = JsonDocument.Parse(...);` is enough.

**Fix:** adopt `RequestMapper`'s `.Clone()` convention everywhere, add `using`, and consider a
shared `ReadJson(NpgsqlDataReader, string)` helper so the pattern cannot drift again.

### 1.2 Resource-type keys are validated three different ways

Four code paths answer "is this a valid resource type key?" and they disagree:

| File | Line | Mechanism |
|---|---|---|
| `backend/src/Endpoints/CriteriaEndpoints.cs` | 23 | hard-coded `ResourceTypeKeys.IsKnown` |
| `backend/core/Validators/CreateCriterionRequestValidator.cs` | 37 | hard-coded `ResourceTypeKeys.IsKnown` |
| `backend/src/Endpoints/CriterionApplicabilityEndpoints.cs` | 48–57 | **database lookup** via `IResourceTypeRepository` |
| `backend/core/Repositories/CriteriaRepository.cs` | 139–152 | **database lookup**, throws on unknown key |

`resource_types` is a table carrying an `is_system` flag (migration 1300) — the schema clearly
anticipates non-system rows — yet two of the four paths refuse anything outside the hard-coded
`{space, person, tool}` set. The comment at `CriteriaRepository.cs:136` asserts that upstream
validation guarantees known keys, which holds only for the two hard-coded paths.

Worth fixing independently of the resource-types feature: it is an inconsistency, and the DB
lookup is already the de-facto authority because it runs last and throws.

**Fix:** resolve the two hard-coded paths through `IResourceTypeRepository.GetByKeyAsync`, and
re-document `ResourceTypeKeys` as "system types only" rather than "the known set". This is
exactly what `claude/generic-resource-types-r9m82d` does, if cherry-picking is easier.

### 1.3 `CLAUDE.md` documents a pre-push hook that does not exist

`CLAUDE.md` states twice that `dotnet format` is enforced locally:

- line 20 — "`dotnet format` must pass before push (enforced by `.githooks/pre-push` …)"
- line 104 — "Don't modify `.githooks/pre-push` ad-hoc …"

`.githooks/` is **not in the working tree and not tracked by git** (`git ls-files .githooks`
returns nothing, and it is not gitignored). There is no hook-installer script under `scripts/`.

The guardrail itself is intact — CI enforces it at `.github/workflows/release-ci.yml:229`
(`dotnet format Orkyo.Foundation.slnx --verify-no-changes --no-restore`). Only the *local* half
is missing.

Severity is low for humans but higher for agents: `CLAUDE.md` is the agent-facing contract, and
an agent that believes formatting is enforced pre-push will skip running it and fail CI instead.

**Fix:** either commit the hook plus an installer that sets `core.hooksPath`, or correct
`CLAUDE.md` to say formatting is enforced in CI only. The file already signals an intent to move
to `pre-commit`, which would resolve this properly.

---

## 2. Structural limits that block generic resource types

These are not defects — they are deliberate designs that predate user-defined types. They are
listed because each silently excludes any type beyond the built-in three, and **two of them
already exclude `tool`**, which has been a seeded system type since migration 1300.

### 2.1 Insights cannot represent a fourth resource type

`backend/core/Services/Insights/InsightsService.cs:60–66` populates a `UtilizationSummary` whose
DTO has three fixed properties — `SpacesPercent`, `PeoplePercent`, `ToolsPercent`. A fourth type
has nowhere to go. This is a DTO-shape constraint, not a filter that can be relaxed.

**Fix direction:** replace the three properties with a keyed collection
(`Dictionary<string, decimal>`, or a `{ typeKey, displayName, percent }[]`) driven by the
`resource_types` rows. Breaking response-shape change, so it wants its own PR.

### 2.2 Search indexes a fixed entity list — `tool` is already missing

`backend/core/Constants/SearchEntityTypes.cs` enumerates `space, request, group, site, template,
criterion, person` — **no `tool`**.

Indexing is done by per-entity SQL triggers rather than a generic resource trigger:
`1280.foundation.search.sql` (spaces, requests, …) and `1510.foundation.search_people.sql`
(people). So **tools are unsearchable on `main` today**, and any user-defined type would be too.

**Fix direction:** a single generic trigger over `resources` writing `entity_type = 'resource'`
with the type key as a facet, retiring the per-type triggers over time.

### 2.3 `resources.metadata_json` was dead schema

The column has existed since `1300.foundation.resource_model_parallel.sql` but was not selected,
mapped, or written anywhere on `main`. Recorded so it is not mistaken for something that was
dropped: the feature branch is what finally uses it, as the store for custom field values.

### 2.4 Resource request DTOs have no shape validator

`backend/tests/Architecture/RequestValidatorCoverageTests.cs` (lines 48 and 58) allowlists
`Api.Models.CreateResourceRequest` and `Api.Models.UpdateResourceRequest` under "transitional
baseline: carry real invariants, want a validator". Still true on `main`, and worth knowing
because that allowlist is what stands between these DTOs and the project's own validation
convention.

---

## 3. Not verified in the remote environment

The remote container has **no .NET SDK** — `dotnet` is not on `PATH`, and no SDK is installed
under `/usr/share/dotnet`, `/usr/lib/dotnet`, or `~/.dotnet`. On
`claude/generic-resource-types-r9m82d` this means:

- the backend was **never compiled**;
- the ~60 added backend tests were **never executed**;
- `dotnet format` was **never run**, so the CI format gate is unverified.

The frontend *was* fully verified in-environment: **3,486 tests across 271 files pass**, with
clean `npm run typecheck` and `npm run lint`.

Before merging that branch, run:

```bash
dotnet build   Orkyo.Foundation.slnx
dotnet test    Orkyo.Foundation.slnx
dotnet format  Orkyo.Foundation.slnx --verify-no-changes --no-restore
./scripts/test-downstream.sh
```

Highest-risk spots if something fails, in likelihood order: the new `ResourceTypeFieldRepository`
SQL (JSONB parameter binding), migration 1650 applying to a fresh Testcontainers database, and
the metadata round-trip through `ResourceRepository`.

---

## 4. Errors in the feature branch (already fixed)

Caught and fixed before the commit. **None exist on `main`**; recorded so the branch history is
legible and the two general lessons stay visible.

| # | Error | How it surfaced |
|---|---|---|
| 1 | Called the 3-arg `EndpointHelpers.ExecuteAsync`, which binds to the `<TRequest, TResult>` overload and wraps the handler's `IResult` in `Results.Ok(...)` — every write endpoint would have returned a nested envelope instead of 201/404 | Reading `EndpointHelpers.cs:30,56` against `JobTitleEndpoints.cs:45` |
| 2 | Untyped null JSONB parameters (`@x::jsonb` with `AddNullable`) relying on Npgsql type inference | Code review; replaced with a typed `AddJsonb` helper in `NpgsqlQueryExtensions` |
| 3 | Three TS errors — `onSubmit={() => canSubmit && submit()}` returns `false \| void`, not `void` | `npm run typecheck` |
| 4 | Five lint errors — dynamic `delete`, `String(unknown)` stringification, non-optional chaining | `npm run lint` |
| 5 | Adding `useResourceTypes` to `SidebarNav` broke **12 existing tests** — the component had never needed a `QueryClient` | `npx vitest run` |
| 6 | Referenced a non-existent `STALE.REFERENCE` tier | `npm run typecheck` |
| 7 | Wrong destructure of `createFeedbackTestQueryClientWithSpy` (`client` vs `queryClient`) | Reading `test-utils.tsx:78` |

Two lessons that generalise beyond the branch:

- **#1 and #2 were caught only by reading neighbouring code**, because no compiler was available.
  That is precisely the class of error section 3 exists to guard against.
- **#5 is a design signal, not just a test fix.** `SidebarNav` was deliberately dependency-light;
  adding a query made it require a `QueryClientProvider` at every render site. It was resolved by
  mocking the hook in `SidebarNav.test.tsx`, consistent with how that file already mocks the store
  and auth context. The alternative — fetching in `AppLayout` and passing nav items down as a prop
  — keeps the component pure and is a reasonable swap if preferred.
