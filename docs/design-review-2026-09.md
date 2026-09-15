# Orkyo design review — September 2026

> **Status:** audit only. No code changes are part of this review. Every finding cites the file and
> line it was read from on 2026-09-15, across the five repos at the commits below. Findings use the
> template from `requirements/orkyo-claude-architecture-review-pack/01-review-spec.md`.

| Repo | Commit | Size |
|---|---|---|
| orkyo-foundation | `45fd203` | 851 C# files / 126k lines · 778 TS files / 126k lines · 129 SQL migrations |
| orkyo-saas | `42f09ca` | 154 C# files / 23k lines · 48 TS files / 8k lines · 27 SQL migrations |
| orkyo-community | `7d64070` | 26 C# files / 2k lines · 12 TS files / 0.9k lines · 4 SQL migrations |
| orkyo-infra | `5975764` | 8 workflows / 3,005 YAML lines · compose, nginx, terraform |
| orkyo-documentation | `a413ad1` | 75 content pages · 2 workflows |

The .NET SDK is not available in the review container, the same constraint as the July review. All
claims are source-read, not compiled.

---

## 1. Executive summary

**The architecture is sound. The liabilities are in the edges: defaults, ratchets, and prose.**

What is in good shape:

- **Layering is clean and acyclic.** `shared ← core ← src(Web)` in foundation; the products are
  adapter sets. Community's whole backend outside migrations is five files and ~600 lines. No
  `Program.cs` exists in foundation, as the placement rule requires.
- **Tenant isolation is structural.** SaaS uses one database per tenant, carried on `OrgContext`
  (`backend/core/Services/OrgContext.cs:7-12`). No tenant repository filters by a tenant column,
  because none needs to. `RepositoryScopingTests` enforces the constructor shape on every repository.
- **The review loop works.** All six candidates of the 2026-05 placement audit are closed. All four
  code findings of the 2026-07 review are fixed, with a regression guard each (§3).
- **Conformance tests are the house style.** Twelve architecture/contract test classes in foundation,
  six in SaaS, four in Community. Convention baselines are ratcheted with staleness tests.
- **Frontend conventions are adopted, not just written.** `meta`-driven mutation feedback is used at
  86 `useMutation` sites and lint-enforced; `qk` query keys at 189 sites; `FormDialog` in 20 dialogs;
  `OrkyoDataTable` at 17 call sites. Zero `window.confirm`.

What this review found, in priority order:

1. **Fail-open defaults and untested edges around tenancy** (§4.1). The image ships with
   `AllowTenantHeader: true` and depends on two external guards. SaaS-only mutating routes are outside
   the only authorization conformance test. The calendar-feed token exposes a site or a tenant, not
   "one user's schedule" as its comment says.
2. **Enforcement that is documented but not installed** (§4.4). `setup.sh` in foundation and community
   sets `core.hooksPath`, which disables the pre-commit `commit-msg` hook that CLAUDE.md calls
   "enforced". The last 30 commits show 3, 5 and 2 non-exempt misses per repo.
3. **Copy-paste at the composition roots and in CI** (§4.2, §4.4). The worker loop, the Valkey
   registration and the Serilog envelope are duplicated between products. ~500 lines of CI YAML are
   duplicated between the product repos. Foundation does not consume four of its own reusable
   workflows. The shared workflows are pinned at four different foundation commits.
4. **Agent-facing documentation that disagrees with the code** (§4.5). Sixteen concrete stale
   statements across CLAUDE.md, README, ARCHITECTURE.md, authorization.md, conventions.md and the
   infra overview. Two of them point new readers to a "current" plan that its own repo marks historical.
5. **Frontend convergence debt** (§4.3). Three loading idioms, hand-rolled empty states beside
   `EmptyState`, pagination on three tables only, DTO types in three places with no mechanical sync,
   33 legacy redirect routes, and two different product-extension mechanisms for the same job.

None of this calls for a rewrite. The recommended first PR (§10) touches shell scripts and prose only.

---

## 2. System complexity map

### 2.1 Backend layering (foundation)

| Project | Files / lines | Depends on |
|---|---|---|
| `backend/shared` (Orkyo.Shared) | 8 / 376 | Configuration.Abstractions |
| `backend/core` (Orkyo.Foundation.Core) | 330 / 37,082 | shared; Npgsql, FluentValidation, OR-Tools, MailKit, Redis, Anthropic |
| `backend/src` (Orkyo.Foundation.Web) | 114 / 12,624 | core; ASP.NET, MCP, Serilog, prometheus-net |
| `backend/migration-abstractions` | 7 / 242 | none |
| `backend/migrator-runtime` | 12 / 1,239 | abstractions; dbup |
| `backend/migrations-foundation` | 2 / 45 + 107 SQL + 23 revert SQL | abstractions, runtime |
| `backend/seeding` | 45 / 6,753 | Bogus, Npgsql |
| `backend/tests` | 321 / 66,286 · 3,099 facts | everything |

Request flow: `src/Endpoints` (44 groups, 56 files, 7.4k lines) → `core/Services` (127 files, 17.5k)
→ `core/Repositories` (50 files, 8.7k). 35 FluentValidation validators. 27 repositories take
`OrgContext`. 7 control-plane repositories take `IDbConnectionFactory`.

The products: SaaS `backend/src` has 73 files (tenant lifecycle, tiers, quotas, rate limiting, bot
protection, marketing surfaces). Community `backend/src` has 5 files. Both `Program.cs` call the same
foundation extension methods in the same order (`InitBootstrapLogger` → `ValidateMode` →
`ConfigurationValidator` → `UseOrkyoLogging` → `AddFoundationServices` → … → `MapFoundationEndpoints`).

### 2.2 Frontend layout (foundation `frontend/src`, 748 files)

| Directory | Files | Notes |
|---|---|---|
| `components/` | 394 | `ui/` 71, `settings/` 83, `utilization/` 64, `requests/` 52 |
| `lib/` | 149 | `api/` 86 (46 hand-written modules over one `apiFetch`), `utils/` 32, `core/` 21 |
| `hooks/` | 83 | 47 hooks + 36 tests |
| `pages/` | 37 | 19 pages |
| `domain/` | 32 | pure logic (scheduling, request tree, plan layout) |
| `store/` 9 · `types/` 7 · `machines/` 2 · `contexts/` 2 | | four zustand stores, one xstate machine |

Products consume `@kymr10n/foundation/src/*` by deep import (no root barrel;
`frontend/package.json:12-23`). Foundation owns the whole route tree in
`components/auth/TenantApp.tsx`. Products inject surfaces (§4.3, F1).

### 2.3 Release train

```
foundation main ──cron 02:00──▶ publish-nightly ──dispatch foundation_updated──▶ saas + community
                                 (0.24.2-nightly.<date>.<sha7>)                   auto-bump → push main
infra release.yml (manual) ──tag foundation vX.Y.Z──▶ stable publish ──dispatch──▶ products pin vX.Y.Z
   └─ await product CI ──▶ deploy.yml staging (tag sha-<40hex>) ──▶ migration classification gate
infra release-promote.yml (manual) ──tag saas+community──▶ saas dispatches saas-release ──▶ prod deploy
   └─ prod ──dispatch advance-classification-baseline──▶ foundation commits .classification-baseline
```

31 workflow files, 7,373 YAML lines: foundation 13 / 2,403 · saas 4 / 807 · community 4 / 1,041 ·
infra 8 / 3,005 · documentation 2 / 117. `deploy.yml` alone is 1,676 lines.

---

## 3. Status of prior reviews

| Review | Item | Status | Evidence |
|---|---|---|---|
| 2026-05 placement audit | 1 Audit service | Closed | `core/Services/IAdminAuditService.cs`; both products register it |
| | 2 Quota enforcer | Closed | `core/Security/Quotas/IQuotaEnforcer.cs`; SaaS `TierBasedQuotaEnforcer`, Community `NoOpQuotaEnforcer` |
| | 3 Break-glass store | Closed | `core/Services/IBreakGlassSessionStore.cs` + `NullBreakGlassSessionStore` |
| | 4 Rate limiting | Partially closed | policy names in `src/Configuration/RateLimitingServiceExtensions.cs`; the store/middleware layer stays SaaS-only, by a comment at `orkyo-community/backend/api/Program.cs:140-141` |
| | 5 Tenant resolvers | Closed as adapters | `ITenantResolver` in core; `SubdomainResolutionStrategy` in src |
| | 6 Connection factories | Closed as adapters | `IDbConnectionFactory` in core; SaaS `MultiTenantDbConnectionFactory` |
| 2026-07 findings | 1.1 JsonDocument disposal | Fixed | `core/Helpers/ReaderExtensions.cs:54-75` |
| | 1.2 Resource-type keys | Fixed | `CriteriaEndpoints.cs:22-25`; guard at `tests/Endpoints/ResourceCapabilityValueTests.cs:71` |
| | 1.3 CLAUDE.md `.githooks/pre-push` | **Open** | `CLAUDE.md:20` and `:165` still name it; `.githooks/` does not exist in foundation |
| | 2.1–2.4 | Fixed | `InsightsModels.cs:90`; generic search trigger 1690; `ResourceRequestValidators.cs` |

---

## 4. Findings

Severity: Critical / High / Medium / Low. Category: Remove / Merge / Simplify / Standardize / Defer /
Do Not Touch.

### 4.1 Security and tenant isolation

#### Finding S1: `AllowTenantHeader` ships as `true` in the SaaS image

Severity: Medium · Category: Standardize · Area: Security · Migration risk: Low

Affected files:
- `orkyo-saas/backend/api/appsettings.json:16`
- `orkyo-foundation/backend/src/Middleware/TenantMiddlewareOptions.cs:27-30`
- `orkyo-foundation/backend/src/Services/SubdomainResolutionStrategy.cs:28-34`
- `orkyo-foundation/backend/core/Configuration/ConfigurationValidator.cs:34-36`
- `orkyo-infra/compose/base/docker-compose.core.yml:112`, `docker-compose.slot.yml:72`

Current state: the option defaults to `false` in code with the comment "production enforces
host-based resolution only". The base `appsettings.json` that is baked into the image sets it to
`true`. With it on, `X-Tenant-Slug` and `?tenant=` select the tenant. Two external guards make the
deployed stack safe: the infra compose files set `TenantResolution__AllowTenantHeader=false` for
every slot, and the validator refuses startup when the flag is on **and** the environment name is
exactly `Production`.

Why this matters: the safe state depends on two files in another repo and on an exact environment
string. A new compose slot, a local `docker run` of the image, or a `Staging`-named environment
without the override runs fail-open. Secure defaults belong in the artifact, not in the deployment.

Recommended target state: `appsettings.json` carries `false` (or omits the key). Local development
turns it on in `appsettings.Development.json` or via `dev.sh`, which already exports environment
variables. The validator stays as a second line.

Implementation notes: one JSON line in saas. Confirm `dev.sh` and `compose.local.yml` set the flag
for local work. `CrossTenantIsolationTests` already exercises header resolution.

Suggested validation: `saas tests/Security/CrossTenantIsolationTests.cs` green. A local run against
`compose.local.yml` still resolves a tenant by header.

#### Finding S2: calendar-feed token scope is documented as one user, implemented as site or tenant

Severity: Medium · Category: Standardize · Area: Security · Migration risk: Low

Affected files:
- `orkyo-foundation/backend/src/Endpoints/CalendarFeedEndpoints.cs:47-70`
- `orkyo-foundation/backend/core/Services/CalendarFeedService.cs:17-21`

Current state: the comment above the anonymous feed route says the token "grants read of one user's
schedule and nothing else". The handler calls `feedService.GetEventsAsync(stored.SiteId, …)`. The
service contract is "scheduled requests inside the feed window, optionally narrowed to one site". A
token with a null `SiteId` serves the whole tenant's schedule.

Why this matters: the token is the only credential on an anonymous route. Reviewers and the
documentation reason about its blast radius from the comment. A leaked feed URL exposes every
scheduled request of a site or of the tenant, not one person's calendar.

Recommended target state: either the comment and `docs/user-guide` state the real scope, or the token
carries `UserId` and the query filters to that user's assignments. The second is the smaller
credential and matches the comment.

Implementation notes: `calendar_feed_tokens` already has a `user_id` for revocation scoping
(`AuthorizationContractTests.cs:41-44`). The choice is a product decision.

Suggested validation: an endpoint test that a token minted by user A returns no event assigned only to
user B, or an updated comment plus a docs-impact entry.

#### Finding S3: SaaS-only mutating routes are outside the authorization conformance test

Severity: High · Category: Standardize · Area: Tests / Security · Migration risk: Low

Affected files:
- `orkyo-foundation/backend/tests/Authorization/AuthorizationContractTests.cs:46-75`
- `orkyo-foundation/backend/tests/FoundationWebApplicationFactory.cs:675`
- `orkyo-saas/backend/tests/Architecture/RouteInventoryTests.cs:6-13`

Current state: `EveryMutatingApiRoute_IsGovernedByAnAuthorizationConvention` walks the
`EndpointDataSource` of `FoundationWebApplicationFactory`, which maps foundation endpoints only. SaaS
adds its own mutating routes (`/api/tenants`, `/api/admin/tenants`, `/api/admin/break-glass`,
`/api/interest`, `/api/fit-check`, `/api/design-partner`, `/api/auth/demo`). SaaS's
`RouteInventoryTests` asserts presence via a 401 on unauthenticated GET, not governance.
`PublicEndpointTenantSkipTests` keeps the skip list honest but says nothing about write gating.

Why this matters: CLAUDE.md states "a conformance test fails CI otherwise" for the endpoint
conventions. For SaaS routes that is not true. An ungated SaaS write ships green.

Recommended target state: the conformance test runs against each product's real host too. The
cheapest form is a shared test helper in `backend/testsupport` that takes an `EndpointDataSource`
and a prefix allow-list, called from a SaaS `Architecture/AuthorizationContractTests` over
`WebApplicationFactory<Program>`.

Implementation notes: the helper is the existing method body with the allow-list as a parameter.
Community gains the same test for free.

Suggested validation: the new SaaS test fails when one `RequireSiteAdmin()` is removed from
`TenantAdminEndpoints.cs`, then passes.

#### Finding S4: a sentinel `OrgContext` stands in for "no tenant"

Severity: Medium · Category: Simplify · Area: Backend · Migration risk: Medium

Affected files:
- `orkyo-saas/backend/api/Program.cs:103-111`
- `orkyo-foundation/backend/core/Services/TenantSettingsService.cs:43`
- `orkyo-community/backend/api/Program.cs:89-106`

Current state: SaaS registers `OrgContext` as a scoped factory that returns
`{ OrgId = Guid.Empty, OrgSlug = "", DbConnectionString = "" }` when no `TenantContext` is in
`HttpContext.Items`. One consumer interprets the sentinel (`IsSiteContext`). Every other repository
resolved in a tenantless scope constructs `NpgsqlConnection("")` and fails at open. Community builds
its `OrgContext` from options instead, so the two products register the same type two different ways.

Why this matters: correctness of isolation depends entirely on `OrgContext` being populated. The
failure mode for a wrong scope is an untyped 500 rather than a typed refusal, and the sentinel makes
"no tenant" look like a tenant.

Recommended target state: a foundation helper registers `OrgContext` once for both products. The
tenantless case throws a typed `TenantContextUnavailableException` (or returns a nullable
`IOrgContextAccessor`), and `TenantSettingsService` takes an explicit site-context dependency instead
of sniffing `Guid.Empty`.

Implementation notes: this pairs with B1. `IOrgContextAccessor` already exists in the README's list
of abstractions.

Suggested validation: a test that resolves a tenant repository in a `[SkipTenantResolution]` scope
and asserts the typed exception.

#### Finding S5: infra's `.claude/settings.json` embeds production coordinates and a live SQL fix

Severity: Medium · Category: Remove · Area: Security / Build · Migration risk: Low

Affected files:
- `orkyo-infra/.claude/settings.json:12,16-26`

Current state: the `permissions.allow` list contains an SSH command to a hard-coded production IP
that runs `UPDATE orkyo_schema_migrations SET checksum = … WHERE id = '2050.saas.invitations'`
inside the prod Postgres container, plus a dozen entries with absolute `/home/alex/Projects/...`
paths.

Why this matters: infra's own rule is "never hardcode secrets, credentials, URLs, or env-specific
values" (`orkyo-infra/CLAUDE.md:35-36`). A committed pre-approval to rewrite a migration checksum in
production is exactly the operation the immutability rule exists to make deliberate. The list also
does not work for anyone whose checkout is not at that path.

Recommended target state: the allow list holds generic read-only patterns only. The checksum repair
becomes a runbook step under `docs/runbooks/` if it is ever needed again.

Suggested validation: `grep -E '[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+|/home/' .claude/settings.json` is empty.

#### Finding S6: the no-silent-defaults linter exempts the deploy path

Severity: Low · Category: Defer · Area: Build · Migration risk: Low

Affected files:
- `orkyo-infra/scripts/ci/lint-no-silent-defaults.sh:24-34`

Current state: the script's header says it does not scan `infra/deploy/**` (which holds the migration
gate) or `.github/workflows/`, because both need explicit approval to touch. The rule it enforces is
the first convention in infra's CLAUDE.md.

Why this matters: the paths with the highest blast radius are the ones without the guard. This is a
known, documented gap. It is recorded here so it is not forgotten.

Recommended target state: a one-time approved pass that adds `lint-default-ok` markers to the
deliberate fallbacks in `deploy.yml` and `demo-reset.yml`, then extends the scan scope.

#### Finding S7: `docs/authorization.md` describes a gap the code has closed, and miscounts the MCP surface

Severity: Low · Category: Standardize · Area: Docs · Migration risk: Low

Affected files:
- `orkyo-foundation/docs/authorization.md:80, 101, 155-158`
- `orkyo-foundation/backend/core/Services/AutoSchedule/AutoScheduleService.cs:174`
- `orkyo-saas/backend/migrations/sql/controlplane/2290.saas.auto_schedule_quota.sql`
- `orkyo-foundation/backend/tests/Endpoints/McpEndpointsTests.cs:422-431`

Current state: the doc's "Known gap" says `FeatureKeys.AutoSchedule` has no entitlement row and no
endpoint gate. The service calls `EnsureEnabledAsync(FeatureKeys.AutoSchedule)` and SaaS migration
2290 seeds the quota. The doc says the MCP surface is 17 tools. There are 18
`[McpServerTool(Name…)]` attributes. The doc says the write-tool guard asserts against the tools
"the server advertises as destructive". The test asserts against tools that lack `readOnlyHint`,
which is stronger and correct.

Recommended target state: the three sentences match the code. The MCP tool count comes from a test
(`ToolNames.Writes` already exists), not from prose.

### 4.2 Backend design

#### Finding B1: the product composition roots duplicate what foundation can own

Severity: Medium · Category: Merge · Area: Backend · Migration risk: Medium

Affected files:
- `orkyo-saas/backend/worker/Program.cs:72-159` and `orkyo-community/backend/worker/Program.cs:63-125`
- `orkyo-saas/backend/api/Program.cs:155-158` and `orkyo-community/backend/api/Program.cs:78-81`
- the Serilog `try / catch when (ex is not HostAbortedException) / finally` envelope in all four hosts

Current state: `WorkerService` and `CommunityWorkerService` are the same loop: the same
`IWorkerJobCoordinator.RunIfDueAsync` calls for `UserLifecycle` and `AnnouncementBroadcast`, the same
jitter, the same `WorkerSchedulePolicy` delays, the same catch structure, the same comment text
("Cadence state lives in the worker_job_runs journal"). SaaS adds one hourly tenant-lifecycle job. The
Valkey multiplexer registration is three identical lines including the exception message. The
`OrgContext` registration differs in shape (S4).

Why this matters: the placement rule says shared behaviour lives in foundation. A worker fix lands
twice or lands once and drifts. Diff between the two worker files is 67 lines of 284, and the
difference that matters is one job.

Recommended target state: foundation ships `OrkyoWorkerHost` (or `AddOrkyoWorkerLoop(jobs =>
…)`) that takes the job list; SaaS passes three jobs, Community two. `AddOrkyoValkey(configKey)`
registers the multiplexer. The Serilog envelope becomes `OrkyoObservability.RunHostAsync(…)`.

Implementation notes: additive foundation API, then two downstream PRs after the version bump, per
the pinned-package rule in both product CLAUDE.md files.

Suggested validation: `scripts/test-downstream.sh`. The SaaS `ExplicitRegistrationTests` still passes.

#### Finding B2: `MigrationCli` reads the environment directly, so Community's migrator mutates process env

Severity: Low · Category: Simplify · Area: Backend · Migration risk: Low

Affected files:
- `orkyo-community/backend/migrator/Program.cs:12-36`
- `orkyo-foundation/backend/migrator-runtime/MigrationCli.cs`

Current state: `MigrationCli` reads `ConnectionStrings__ControlPlane` via
`Environment.GetEnvironmentVariable`. Community aliases its single database onto that key by writing
process environment variables, and duplicates four env-var name constants because the standalone
migrator references neither `Orkyo.Shared` nor the community config project. Its own comment says so.

Recommended target state: `MigrationCli.RunAsync(args, IConfiguration)` or an explicit
`MigrationCliOptions { ControlPlaneConnectionString }`. Community's migrator shrinks to the SaaS
shape (22 lines).

#### Finding B3: Community selects shared migrations by filename substring

Severity: Medium · Category: Standardize · Area: Database · Migration risk: Low

Affected files:
- `orkyo-community/backend/migrations/CommunityFoundationMigrationModule.cs:26-31`
- `orkyo-community/backend/tests/Migrations/CommunityFoundationMigrationModuleTests.cs:9-35`

Current state: the module filters foundation's tenant-phase migrations with
`s.Id.Contains("feedback", StringComparison.Ordinal)`. The intent is documented and tested: the
tenant-phase feedback create (1240) and drop (1630) must not run against Community's single database
where the control-plane feedback table (1170) lives.

Why this matters: any future foundation tenant migration whose id contains "feedback" is silently
skipped in Community. The test pins the two known ids but cannot see a third.

Recommended target state: an explicit exclusion list of migration ids, or a `Scope` value on
`MigrationScript` (`TenantOnly` / `SharedDatabaseSafe`) that foundation sets at the source. The
abstractions project already carries per-script metadata (`DependsOn`, `SupersededChecksums`).

Suggested validation: the existing module test plus an assertion that every excluded id exists in
foundation's set, so a rename surfaces.

#### Finding B4: `RequestRepository` carries three concerns in 1,377 lines

Severity: Medium · Category: Simplify · Area: Backend · Migration risk: High

Affected files:
- `orkyo-foundation/backend/core/Repositories/RequestRepository.cs`

Current state: 30 public async methods: CRUD, schedule mutation and batch updates, requirements
sub-entity CRUD, and tree algebra (`MoveAsync`, `WouldCreateCycleAsync`, `GetDescendantCountAsync`,
`HasChildrenAsync`, `DeleteSubtreeAsync`). It is the largest file in the backend and sits under the
product's main list page and the plan canvas.

Why this matters: every request feature touches this file. Every touch risks the others. The tree half
has no other home and is the natural cut.

Recommended target state: `RequestTreeRepository` (tree algebra) and `RequestRequirementRepository`
(sub-entities) split out on the next feature that touches them. Not a standalone refactor PR.

Implementation notes: this is a high-risk area (§7). The split is mechanical but the callers are many;
land it behind the existing endpoint tests.

#### Finding B5: sixteen services write SQL. The conventions doc says eight

Severity: Medium · Category: Standardize · Area: Backend · Migration risk: Medium

Affected files:
- `orkyo-foundation/docs/conventions.md:150`
- `orkyo-foundation/backend/core/Services/Preset/PresetApplier.cs` (16 `new NpgsqlCommand`)
- `core/Services/InvitationService.cs`, `UserManagementService.cs`, `SessionService.cs`,
  `Insights/InsightsService.cs`, `Reporting/ReportingQueryService.cs`, and ten more

Current state: 16 files under `core/Services` construct `NpgsqlCommand` directly. `PresetApplier` is
a repository with a service name. `InsightsService` and `ReportingQueryService` are raw-SQL query
services with no repository behind them. The endpoint side is ratcheted
(`tests/Architecture/EndpointDataAccessTests.cs:35-37`, three files). The service side is not.

Recommended target state: the same ratchet for services: a baseline list in `ConventionContractTests`
with a staleness test, so the number only goes down. Rename `PresetApplier`'s data half to a
repository on touch. The conventions doc states the measured number.

#### Finding B6: stated conventions without ratchets have not moved

Severity: Low · Category: Standardize · Area: Backend · Migration risk: Low

Affected files:
- `orkyo-foundation/docs/conventions.md`
- `orkyo-foundation/backend/tests/Architecture/ConventionContractTests.cs`
- `orkyo-foundation/backend/src/Endpoints/Ai/AiConversationEndpoints.cs:62,96`,
  `Ai/AiAllowanceEndpoints.cs:121`, `AccountLifecycleEndpoints.cs:111`
- `orkyo-foundation/backend/core/Services/ICalendarWriter.cs:27`

Measured on this tree:

| Convention | Doc says | Measured | Ratcheted? |
|---|---|---|---|
| `conn` over `db` connection local | ~200 sites, even split | 165 `conn` / 134 `db` | no |
| `DomainLimits` over bare numbers | ~50 bare | 44 bare `MaximumLength(n)` / 23 `DomainLimits.*` | no |
| bare `Results.NotFound()` only on calendar feed | calendar only | 4 more sites (AI conversations, AI allowance, account lifecycle) | no |
| `I`-prefix means interface | rename on touch | `public static class ICalendarWriter` still | no |
| reads are `Get` | — | ~14 `Fetch*`/`Load*`/`Find*Async` methods | no |
| SQL status literals | ~20 | 22 sites / 11 files | **yes** (baseline + staleness test) |
| `KeyNotFoundException` | done | 0 | **yes** |
| `^`/`$` anchors | done | 0 | **yes** |

The ratcheted conventions are at zero or frozen. The unratcheted ones are where the doc left them.
The mechanism that works is already in the repo. The fix is to apply it to the remaining rows, not to
sweep.

Recommended target state: one baseline test per row above, each with a staleness assertion, added in
one PR that changes no production code. The doc's "eight" and "calendar only" become the measured
values.

#### Finding B7: a 23-file `revert/` tree with no visible consumer

Severity: Low · Category: Remove · Area: Database · Migration risk: Low

Affected files:
- `orkyo-foundation/backend/migrations-foundation/revert/*.revert.sql` (23 files)

Current state: no code in `migrator-runtime`, no script under `scripts/`, no workflow in foundation
or infra references the directory or the `.revert.sql` suffix. `deploy.yml` rolls back by snapshot,
not by revert script.

Recommended target state: either a runner and a runbook step that uses them, or deletion with a note
in `orkyo-infra/docs/migrations/classification.md` that rollback is snapshot-based. Twenty-three
untested SQL files that claim to undo applied migrations are a liability if someone trusts them.

#### Finding B8: `migrator-runtime` has one test class in foundation

Severity: Low · Category: Defer · Area: Tests · Migration risk: Low

Current state: `tests/Migrations/MigrationAbstractionsContractTests.cs` covers the value objects.
`AdvisoryLock`, `ChecksumPolicy`, `MigrationOrderer` and `LegacyAdoptionBaseline` (1,239 lines) are
covered end-to-end downstream (`saas tests/Migrations/MigratorEndToEndTests.cs`,
`tests/Integration/LegacyAdoptionTests.cs`). A checksum-policy regression surfaces in SaaS CI, not
where the code lives.

Recommended target state: unit tests for `ChecksumPolicy` and `MigrationOrderer` in foundation on the
next change to either. Not a sweep.

### 4.3 Frontend design

#### Finding F1: two mechanisms for product-specific surfaces

Severity: Medium · Category: Standardize · Area: Frontend · Migration risk: Low

Affected files:
- `orkyo-saas/frontend/src/App.tsx:41, 82-90`
- `orkyo-community/frontend/src/App.tsx:28-38`
- `orkyo-foundation/frontend/src/components/auth/TenantApp.tsx`

Current state: SaaS injects its admin page, tenant-select page, account tabs and onboarding content
as render-prop slots on `TenantApp`. Community intercepts `pathname` before rendering `TenantApp`
and renders `CommunityAdminPage` itself. `frontend/ARCHITECTURE.md` describes a third shape (a
wrapper that branches on `/admin/*`), which neither product uses.

Recommended target state: one mechanism, the slot API, for both products. Community passes
`renderAdminPage`. ARCHITECTURE.md describes the slot API and nothing else.

#### Finding F2: 33 legacy redirect routes live in the route tree

Severity: Low · Category: Remove · Area: Frontend · Migration risk: Low

Affected files:
- `orkyo-foundation/frontend/src/components/auth/TenantApp.tsx:185-300`

Current state: `/spaces`, `/people`, `/floorplan` and their sub-routes are `Navigate` or
`LegacyTypeRedirect` shims into the generic `ResourceClassPage`. 33 such lines. No date or version
marks when they retire.

Recommended target state: a retirement version in a comment at the top of the block, and removal in
the next major. The documentation repo's "a URL is a commitment" rule applies to published docs, not
to in-app routes that the sidebar no longer emits.

#### Finding F3: three loading idioms and hand-rolled empty states beside the primitives

Severity: Medium · Category: Standardize · Area: UX · Migration risk: Low

Affected files:
- `orkyo-foundation/docs/UI-GUIDELINES.md:8-12` (the rule)
- `LoadingSpinner` in 37 files. Raw `<Loader2>` at 18 sites in 14 files; `<Skeleton>` in 1 file
- `EmptyState` in 10 files. Hand-rolled `text-center … text-muted-foreground` blocks in
  `settings/TemplateDialogBase.tsx:304`, `requests/RequestRequirementsSection.tsx:93`,
  `resource-groups/ResourceGroupMembersEditor.tsx:222,276`, `pages/AccountPage.tsx:625`,
  `insights/BottlenecksTab.tsx:223`, `utilization/ResourceAssignmentDialog.tsx:462,620`, and three
  identical `rounded-lg border border-dashed p-8 text-center` blocks in `settings/ReportingApiSettings.tsx:216`,
  `settings/AiAssistantSettings.tsx:368`, `settings/PlatformApiSettings.tsx:304`

Current state: the guideline says "Do not hand-roll `<div className="text-center py-8
text-muted-foreground">`". More files hand-roll it than use `EmptyState`. Loading has a two-tier rule
(skeleton for layout, spinner for actions) and three idioms in practice.

Recommended target state: an ESLint `no-restricted-syntax` rule for the literal class combination
and for `Loader2` outside `ui/`, the same way `toast.*` inside `onSuccess` is already banned
(`frontend/eslint.config.js:41-54`). The rule lands with the current sites as `eslint-disable`
comments, so the number only goes down.

#### Finding F4: pagination on three tables

Severity: Medium · Category: Standardize · Area: UX · Migration risk: Low

Affected files:
- `components/admin/AuditLogTab.tsx`, `components/requests/RequestListView.tsx`,
  `components/resource-groups/ResourceGroupList.tsx` (paged)
- sites, criteria, users, resource types, templates, resource instances (unpaged)

Current state: `OrkyoDataTable` supports pagination. Three call sites use it. The backend has
`QueryPagedAsync` and `PagedResult<T>`. Every other list renders everything it fetched.

Why this matters: the July demo-seed work made a "demo shop that is actually busy". Unpaged lists
are the first thing that degrades with real data, and they degrade differently per page.

Recommended target state: a per-list decision recorded in `docs/resource-navigation-and-lists-spec.md`:
paged (with the backend endpoint returning `PagedResult`) or virtualized. Resource instances first.

#### Finding F5: three time-grid engines, two canvases, two calendar libraries

Severity: Medium · Category: Defer · Area: Frontend · Migration risk: High

Affected files:
- `components/utilization/SchedulerGrid.tsx` (482), `TimelineGridShell.tsx`, `ResourceUtilizationGrid.tsx`
- `components/requests/SpaceDrawingCanvas.tsx` (597), `components/requests/plan/SitePlanCanvas.tsx` (753)
- `components/utilization/RequestCalendar.tsx` (FullCalendar), `components/ui/calendar.tsx` (react-day-picker)

Current state: each time grid has its own header row with the same
`text-center text-xs font-medium text-muted-foreground` cell markup (`TimelineGridShell.tsx:207`,
`SitePlanCanvas.tsx:602`, two more). The two canvases share no code. FullCalendar (five peer
packages) serves one component.

Recommended target state: not a merge. Extract the header cell and the scale/anchor plumbing into one
`TimeAxisHeader` used by all three grids. Leave the engines. Record FullCalendar as a deliberate
single-component dependency in `frontend/docs/COVERAGE.md` or `package.json` comments.

#### Finding F6: DTO types in three places, none checked against the backend

Severity: Medium · Category: Standardize · Area: Frontend · Migration risk: Medium

Affected files:
- `frontend/contracts/` (9 files, constants only, aligned by `contract-alignment.test.ts`)
- `frontend/src/types/` (7 files: `requests.ts`, `criterion.ts`, `site.ts`, …)
- `frontend/src/lib/api/*.ts` (180 exported `interface`/`type` declarations. 19 in `admin-api.ts`)

Current state: `contracts/` is small and mechanically aligned with named backend files. The domain
DTOs are hand-written in two other places with no alignment test and no OpenAPI generation. Criteria
has types in `types/criterion.ts` and in `lib/api/criteria-api.ts`. Swagger is already wired on the
backend (`AddOrkyoReportingSwagger`), so the schema exists at runtime.

Recommended target state: one home for DTOs (`types/` by domain, `lib/api/` imports from it), and one
`ApiPathContractTests`-style test on the backend side that compares the OpenAPI schema property names
of the top ten DTOs with a checked-in snapshot. Full generation is not required.

#### Finding F7: state plumbing has grown a second path for three things

Severity: Low · Category: Simplify · Area: Frontend · Migration risk: Low

Affected files:
- `frontend/src/store/app-store.ts:7-39` (site selection + timeline viewport + collapse flags + theme)
- `frontend/src/store/index.ts` (exports three of four stores; `ui-actions-store` is missing)
- `frontend/src/lib/core/invalidate-request-data.ts` (imperative invalidation beside `meta.invalidates`)
- `frontend/src/components/resources/ResourceScheduleDialog.tsx:129` (raw literal query key beside `qk`)
- `frontend/src/machines/authMachine.ts` (559 lines. The only xstate machine)

Recommended target state: `app-store` splits into `timeline-store` (scale/anchor/cursor/spaceOrder)
and `chrome-store` (collapse flags, theme) on touch. The barrel exports all stores;
`invalidate-request-data` is either the implementation behind a `meta.invalidates` alias or is
deleted. The literal key moves to `qk`. Xstate stays: one machine for the one genuinely stateful
flow is the right size.

#### Finding F8: dialog and feedback outliers outside the documented exemptions

Severity: Low · Category: Standardize · Area: Frontend · Migration risk: Low

Affected files:
- raw `<DialogContent>` outside `ui/` in 14 files; `docs/dialog-feedback.md:20-26` exempts one of
  them by name (`ResourceGroupMembersEditor`). Form-shaped and unlisted: `settings/PresetSettings.tsx`,
  `admin/AnnouncementsTab.tsx`, `admin/FeedbackTab.tsx`, `settings/api-tokens/token-ui.tsx`,
  `utilization/ScheduleSlotDialog.tsx`, `utilization/ResourceAssignmentDialog.tsx`,
  `requests/FloorplanUploadDialog.tsx`
- `components/settings/AiAssistantSettings.tsx` (11 `toast.` calls, no `useMutation`)
- `pages/RequestsPage.tsx:318-352` (6 `toast.` calls, manual reload. The documented exception covers
  the product's main list page)

Recommended target state: each of the seven dialogs is either moved onto `FormDialog` on touch or
added to the exemption list with a reason. `AiAssistantSettings` adopts `useMutation` + `meta`.
`RequestsPage` is the one worth a dedicated change: it is the highest-traffic CRUD in the product.

#### Finding F9: two dead product dependencies and peer-range skew

Severity: Low · Category: Remove · Area: Build · Migration risk: Low

Affected files:
- `orkyo-saas/frontend/package.json:44,48`, `orkyo-community/frontend/package.json:43,47`
- `orkyo-foundation/frontend/src/components/ui/label.tsx:7`, `separator.tsx:8`

Current state: foundation replaced `@radix-ui/react-label` and `@radix-ui/react-separator` with
native elements (the file headers say so). Both products still declare them. Foundation's peer ranges
lag the products (`@tanstack/react-table` `^9.1.2` vs `^9.2.4`; `lucide-react` `^1.20.0` vs `^1.38.0`).
`check:dedupe-sync` catches missing peers, not stale ones or range drift.

Recommended target state: remove the two packages from both products. Extend `check-dedupe-sync.mjs`
to flag product dependencies that no foundation or product source imports.

#### Finding F10: three `utils` entry points

Severity: Low · Category: Simplify · Area: Frontend · Migration risk: Low

Affected files: `frontend/src/lib/utils.ts`, `lib/utils/index.ts`, `lib/utils/utils.ts`; `lib/formatters.ts`

Recommended target state: `lib/utils/index.ts` is the only barrel; `lib/utils.ts` is deleted after a
codemod of its importers; `formatters.ts` moves under `lib/utils/` since ESLint already names it as
the single date-format source (`eslint.config.js:208`).

#### Finding F11: end-to-end coverage is requests and mobile only

Severity: Low · Category: Defer · Area: Tests · Migration risk: Low

Current state: five Playwright specs (`smoke`, `mobile`, `requests-visual`, `requests-dialog-visual`,
`requests-dialog-dirtyguard`). `UtilizationPage` (1,174 lines, the largest page), floorplan, insights,
settings and admin have no e2e. Unit adjacency is high (325 test files; `components/ui/` weakest at
24 tests for 47 sources; `components/fields/` has none).

Recommended target state: one smoke spec per top-level nav item, asserting render and the primary
empty state. Not visual regression.

#### Finding F12: two files named `UI-GUIDELINES.md` with different content

Severity: Low · Category: Merge · Area: Docs · Migration risk: Low

Affected files:
- `orkyo-foundation/docs/UI-GUIDELINES.md` (89 lines: presentation primitives)
- `orkyo-foundation/frontend/docs/UI-GUIDELINES.md` (510 lines: "canonical UI coding rules")
- `frontend/docs/UI-GUIDELINES.md:222` names a `planned` request status. The enum is
  `new | in_progress | done | cancelled | deferred` (`frontend/src/types/requests.ts:45`)

Recommended target state: one file. The 89-line primitives section becomes a heading in the
510-line one, and `docs/UI-GUIDELINES.md` becomes a one-line pointer. The status palette lists the
real enum.

### 4.4 Cross-repo, build and release

#### Finding X1: the enforced docs-impact hook is not installed in two of three repos

Severity: High · Category: Standardize · Area: Build · Migration risk: Low

Affected files:
- `orkyo-foundation/setup.sh:20` and `orkyo-community/setup.sh:38` (`git config core.hooksPath .githooks`)
- `orkyo-saas/setup.sh:56-62` (documents the bug and clears `core.hooksPath`)
- `orkyo-community/.pre-commit-config.yaml:8` (`default_install_hook_types` lacks `pre-push`. Hooks
  at lines 65 and 73 are `stages: [pre-push]` and never install)
- `orkyo-foundation/.pre-commit-config.yaml:8` (same omission. Hooks at lines 71 and 79)
- `orkyo-foundation/CLAUDE.md:180`, `orkyo-saas/CLAUDE.md:114`, `orkyo-community/CLAUDE.md:112`

Current state: pre-commit installs into `.git/hooks`. Git ignores `.git/hooks` whenever
`core.hooksPath` is set. Foundation's `setup.sh` sets it to a directory that does not exist in
foundation. Community's sets it to a directory with one legacy `pre-push`. In both, the `commit-msg`
docs-impact hook and the pre-push format/test hooks never fire. SaaS found and fixed this in its own
script and left the other two.

Measured on the last 30 commits, excluding the exempt prefixes: foundation 3 misses, saas 5,
community 2. Dependabot group bumps have unprefixed subjects ("Bump the nuget-minor-patch group…")
and escape the exemption list by accident.

Why this matters: every product CLAUDE.md says the hook "blocks commits". The published documentation
quotes UI strings verbatim. The trailer is the mechanism that keeps it honest. The mechanism is off.

Recommended target state: the saas `setup.sh` block (clear `core.hooksPath`, `pre-commit install
--install-hooks`) copied to foundation and community; `pre-push` added to both install-type lists;
`orkyo-community/.githooks/` deleted; `scripts/check-docs-impact.sh` treats a `Bump ` subject as
`chore:`.

Suggested validation: `git commit` of a change under `backend/src/Endpoints` without the trailer is
rejected in each repo after `./setup.sh`.

#### Finding X2: CI YAML is duplicated across repos and pinned at four foundation commits

Severity: Medium · Category: Merge · Area: Build · Migration risk: Medium

Affected files:
- `orkyo-saas/.github/workflows/release-ci.yml:58-512` and `orkyo-community/.github/workflows/release-ci.yml:111-568`
- `orkyo-foundation/.github/workflows/release-ci.yml:104,145,173,402` (inline `audit-*` and
  `container-scan` jobs beside `reusable-audit-*.yml` and `reusable-container-scan.yml`)
- `orkyo-foundation/.github/workflows/security-refresh.yml` (166 lines) beside `reusable-security-refresh.yml` (139)
- reusable-workflow pins: `a367c81` (7 uses), `a114f8a` (2), `3a429ca` (1), `dff2d1e` (2)

Current state: `detect-changes`, `backend-ci`, `frontend-ci`, `smoke-tests`, `build-images` and
`container-scan` are ~500 lines of near-identical YAML in each product. Foundation consumes one of
its seven reusable workflows. The products pin the shared workflows at four different foundation
SHAs. Saas pins `audit-secrets` and `audit-nuget` at two different commits within one file.

Recommended target state: `reusable-product-ci.yml` in foundation with inputs for the product name
and ports. Both products call it. Foundation's own jobs call its own reusables. One pin per repo,
bumped by Dependabot's `github-actions` ecosystem (already enabled).

Suggested validation: a PR in each product whose only change is the workflow swap, with the same
job names so branch protection is unchanged.

#### Finding X3: local tooling diverges where it claims parity

Severity: Low · Category: Standardize · Area: Build · Migration risk: Low

Affected files:
- `orkyo-saas/dev.sh` (567 lines) vs `orkyo-community/dev.sh` (337): 292 differing lines
- `orkyo-saas/compose.local.yml:43` (`KC_DB: dev-file`, persisted) vs `orkyo-community/compose.local.yml:38` (`KC_DB: dev-mem`)
- both `compose.local.yml`: floating `postgres:16-alpine`, `valkey/valkey:8-alpine`, `quay.io/keycloak/keycloak:26.6.4`
- `.claude/` is byte-identical in foundation, saas, community and documentation (9 files each)

Current state: the CLAUDE.md files present `dev.sh` as the same tool with different ports. The
difference is a 220-line SaaS site-admin provisioning block, different connection-string shapes, and
a different local Keycloak persistence contract. Four verbatim copies of `.claude/` (including the
six-file vendored `simple-english` skill) with no sync check, while the marketing shell has one.

Recommended target state: the shared `dev.sh` subcommand surface in one script under
`orkyo-foundation/scripts/` sourced by both; `KC_DB` aligned. A `check:claude-dir` job or a
vendoring note in `orkyo-documentation`'s `UPSTREAM.md` pattern.

#### Finding X4: the cross-repo coupling register

Severity: Low · Category: Do Not Touch · Area: Build · Migration risk: —

These are the points where one repo depends on another's layout or names. None is wrong. Each is a
place where a rename breaks a different repo. Recorded so the next rename checks the list.

| Coupling | Producer | Consumer |
|---|---|---|
| `foundation_updated` dispatch | foundation `release-ci.yml:745,753,1005,1015` | saas `release-ci.yml:24,651`; community `:27,879` |
| `advance-classification-baseline` dispatch | infra `deploy.yml:1135` | foundation `advance-classification-baseline.yml:18` |
| `saas-deploy` / `saas-release` dispatch | saas `release-ci.yml:549,626` | infra `deploy.yml:10` |
| Migration SQL path | foundation `backend/migrations-foundation/sql/` | infra `deploy.yml:210` |
| Baseline file path | foundation `advance-classification-baseline.yml:64` | infra dispatch payload |
| KC image tag by grep of another repo | saas `Directory.Build.props:3` | infra `deploy.yml:328` |
| Reusable-workflow paths + SHAs | foundation `.github/workflows/reusable-*.yml` | saas/community, 12 uses |
| `scripts/bump-foundation.sh` | saas/community | foundation `reusable-auto-bump.yml:38` |
| `DeploymentConfig.cs` shape | foundation `backend/core/` | infra `pr-checks.yml:228-233` (`continue-on-error`) |
| Marketing shell hash | saas `check-marketing-routes.mjs:80` | documentation `check-vendored-shell.mjs:28` |
| Sibling-directory csproj | foundation project paths | saas/community `*.csproj` `..\..\..\orkyo-foundation\…` |
| `GHCR_TOKEN` single PAT | 16 workflow files | GHCR auth, cross-repo checkout, dispatch, ruleset-bypass push |
| `release/**` = hotfix line | saas `release-ci.yml:20,428` | infra `docs/runbooks/deploy.md:101-117` |

The one item worth a change: one PAT for five distinct privileges is a single point of compromise.
Fine-grained tokens per purpose are a bounded piece of work.

#### Finding X5: plan documents accumulate without status

Severity: Low · Category: Remove · Area: Docs · Migration risk: Low

Affected files:
- `orkyo-infra/docs/`: `optimization-plan-2026-07.md` (complete), `-07b.md` (shipped),
  `optimization-review-2026-07-16.md` (no status), `remediation-plan-2026-07.md` (status line says
  "superseded by the header above"), `monitoring-upgrade-plan-2026-08.md` (staging only),
  `pagespeed-improvement-plan-2026-08.md` (round two undeployed), `turnstile-plan-2026-08.md`,
  `structural-hardening-2026-05.md` (historical). Root `release-log-20260902.md`
- `orkyo-foundation/requirements/` three non-archived packs; `orkyo-saas/requirements/` two packs and
  `orkyo-plan-matrix.json`. Both CLAUDE.md files call the folder "historical spec packs"

Recommended target state: `orkyo-infra/docs/plans/` with a status line as the first line of every
file, and the completed ones moved there. Foundation's `COMPLETIONS_INDEX.md` pattern applied to the
three open packs (archive or mark active).

### 4.5 Documentation accuracy

#### Finding D1: sixteen stale statements in the agent-facing contracts

Severity: Medium · Category: Standardize · Area: Docs · Migration risk: Low

Every item was verified against the tree. These files are what a contributor or an agent reads first.

| # | File | Says | Reality |
|---|---|---|---|
| 1 | `orkyo-foundation/CLAUDE.md:20,165` | `.githooks/pre-push` enforces `dotnet format` | no `.githooks/` in foundation; `.pre-commit-config.yaml` does it (open since July) |
| 2 | `orkyo-foundation/CLAUDE.md:92`, `orkyo-saas/CLAUDE.md:59`, `orkyo-community/CLAUDE.md:57` | `structural-hardening-2026-05.md` is the "current cross-repo hardening plan" | `orkyo-infra/CLAUDE.md:67` marks it "historical … context only" |
| 3 | `orkyo-foundation/README.md:61-71` | tree has `backend/migrations/` | it is `migrations-foundation/`, plus `core/`, `migration-abstractions/`, `migrator-runtime/`, `seeding/`, `testsupport/` |
| 4 | `frontend/ARCHITECTURE.md:73-77` | SaaS imports via `@foundation/src/...` | `orkyo-saas/frontend/INTEGRATION.md:18-24` forbids that alias; products import `@kymr10n/foundation/src/...` |
| 5 | `frontend/ARCHITECTURE.md:130` | a bridge module at `saas/src/lib/foundation/` | does not exist; `saas/frontend/src/lib/` holds `api/` and `generated/` |
| 6 | `frontend/ARCHITECTURE.md` | foundation `App.tsx` with `LocalDevShell` | foundation has no `App.tsx`; each product owns that logic |
| 7 | `frontend/ARCHITECTURE.md` | routes "requests, spaces, scheduling, feedback" | `/spaces` is a redirect into `ResourceClassPage` |
| 8 | `orkyo-saas/docs/architecture.md:31` | "ASP.NET Core 9" | .NET 10 (`global.json`) |
| 9 | `orkyo-saas/docs/architecture.md:596` | Observability "SaaS only — Community will gain parity (planned)" | Community calls `UseOrkyoLogging` and `MapOrkyoMetricsEndpoint` (`api/Program.cs`) |
| 10 | `docs/authorization.md:80` | 17 MCP tools | 18 |
| 11 | `docs/authorization.md:155-158` | auto-schedule has no endpoint gate | `AutoScheduleService.cs:174` gates it; saas migration 2290 seeds the row |
| 12 | `docs/conventions.md:150` | "Eight" services write SQL | 16 |
| 13 | `docs/conventions.md` | bare `Results.NotFound()` only on calendar feed | four more sites (B6) |
| 14 | `orkyo-infra/docs/00-overview.md:62-76` | layout without `compose/base/`, `compose/nginx/`, `infra/`; keycloak dir has a Dockerfile | `compose/base/` holds the stacks; `compose/keycloak/` holds `orkyo-realm.json` only |
| 15 | `orkyo-infra/docs/structural-hardening-2026-05.md:18,38,62,70,81,143,145` | `scripts/backup.sh`, `scripts/restore.sh`, `scripts/check_migration_classification.sh`, `compose/prod/docker-compose.core.yml`, "dispatches to saas only" | none of the four paths exists; both dispatches exist |
| 16 | `orkyo-documentation/CLAUDE.md:7` | Node `>=22.12` | `package.json:32` `>=24.0`; `.nvmrc` 24 |

Recommended target state: all sixteen corrected in one docs PR per repo (§10). For the numbers
(items 10, 12, 13) the doc cites the test that owns the number instead of the number.

---

## 5. Top-10 lists

### 5.1 Simplification opportunities

1. One worker host in foundation, two job lists in the products (B1).
2. `MigrationCli` takes configuration, Community migrator shrinks to 22 lines (B2).
3. Explicit migration scope instead of a filename substring (B3).
4. `OrgContext` registered once by foundation, typed failure instead of a sentinel (S4).
5. `RequestRepository` split on the next touch (B4).
6. `TimeAxisHeader` shared by the three grids (F5).
7. `app-store` split into timeline and chrome (F7).
8. One `utils` barrel (F10).
9. One product-extension mechanism (F1).
10. One `UI-GUIDELINES.md` (F12).

### 5.2 DRY violations

1. Worker loop duplicated in two hosts (B1).
2. ~500 lines of CI YAML per product (X2).
3. Foundation's inline audit jobs beside its own reusable workflows (X2).
4. `.claude/` duplicated verbatim four times (X3).
5. `dev.sh` and `compose.local.yml` (X3).
6. Valkey + break-glass registration (B1).
7. Serilog envelope in four hosts (B1).
8. Four copies of the grid header cell (F5).
9. DTO types in `types/` and `lib/api/` (F6).
10. Three hand-rolled dashed-border empty blocks in three settings pages (F3).

### 5.3 UX consistency issues

1. Three loading idioms (F3).
2. Hand-rolled empty states outnumber `EmptyState` (F3).
3. Pagination on three tables (F4).
4. `RequestsPage` hand-rolls toasts and reloads while every other CRUD uses `meta` (F8).
5. Seven form-shaped dialogs off `FormDialog` without an exemption (F8).
6. `AiAssistantSettings` with 11 toasts (F8).
7. Ad-hoc search state on two pages beside the table column filter (`pages/RequestsPage.tsx:120`,
   `ResourceGroupMembersEditor.tsx:57`).
8. `BottlenecksTab` is the one list on a raw `<Table>` (`components/insights/BottlenecksTab.tsx`).
9. Two calendar surfaces from two libraries (F5).
10. Community's admin page renders outside the app shell path that SaaS's uses (F1).

### 5.4 Tenant and security risks

1. SaaS mutating routes outside the authorization conformance test (S3) — High.
2. `AllowTenantHeader: true` in the image (S1) — Medium.
3. Calendar-feed token scope (S2) — Medium.
4. Sentinel `OrgContext` (S4) — Medium.
5. Production coordinates and a checksum rewrite in `.claude/settings.json` (S5) — Medium.
6. One PAT for five privileges (X4) — Medium.
7. Deploy path outside the no-silent-defaults linter (S6) — Low.
8. Three admin settings pages rely on the route guard only, by design
   (`ListDefinitionSettings.tsx:22`, `ResourceTypeSettings.tsx:20`, `TypeCatalogSettings.tsx:37`) — Low.
9. `useRequestEditor.tsx:41` deliberately bypasses `useCanEdit()` for break-glass — Low, documented
   in code only.
10. 23 untested revert scripts that claim to undo migrations (B7) — Low.

No cross-tenant data path was found. The isolation model (connection per tenant, no shared tables
for tenant data) holds everywhere it was read.

---

## 6. Dead code and dependency cleanup candidates

| Candidate | Where | Evidence |
|---|---|---|
| `@radix-ui/react-label`, `@radix-ui/react-separator` | both product `package.json` | replaced by native elements in foundation |
| `revert/` tree (23 SQL files) | `backend/migrations-foundation/revert/` | no consumer found |
| 33 legacy redirect routes | `TenantApp.tsx:185-300` | shims into `ResourceClassPage` |
| `orkyo-community/.githooks/pre-push` | community | superseded by `.pre-commit-config.yaml` |
| inline `audit-*`, `container-scan` jobs, `security-refresh.yml` | foundation `release-ci.yml`, `security-refresh.yml` | reusable equivalents exist |
| `lib/utils.ts` shim | foundation frontend | two other entry points |
| `ICalendarWriter` name | `core/Services/ICalendarWriter.cs:27` | static class with an interface prefix |
| `ErrorCodes` (11 refs) | core | superseded by `ApiErrorCodes` (17 refs) |
| `optimization-plan-2026-07*.md`, `remediation-plan-2026-07.md` | infra docs | marked complete or superseded |
| `frontend/package.json` `overrides.exceljs.uuid` | foundation | `exceljs` is a test-only devDependency |

`@supersedes-checksum` is **not** dead: `orkyo-community/backend/migrations/sql/tenant/3010.community.demo_seed.sql` uses it.

---

## 7. High-risk areas not to refactor casually

- **`backend/core/Repositories/RequestRepository.cs`** — 30 public methods under the main list page,
  the plan canvas, the calendar and MCP. Split only with the endpoint tests as the net.
- **`orkyo-saas/backend/src/data/MultiTenantDbConnectionFactory.cs`** and the `OrgContext` factory —
  the isolation boundary. S4 is a shape change. It needs the cross-tenant tests green first.
- **`orkyo-infra/.github/workflows/deploy.yml`** release-identity, migration gate and rollback
  snapshot — protected by an explicit-approval rule in infra's CLAUDE.md.
- **`frontend/src/machines/authMachine.ts`** — 13 states across apex, tenant and local-dev modes.
- **`frontend/src/pages/UtilizationPage.tsx`** and the three grid engines — 15 `useState`, drag
  state in a store, viewport in another store and in the URL.
- **`backend/migrator-runtime`** — checksum policy and advisory lock are covered only downstream (B8).
- **`backend/migrations-foundation/sql/`** — immutable by rule; the checksum ratchet makes any edit a
  production incident.

---

## 8. Recommended target patterns

Every pattern below already exists in the repo. The recommendation is that each becomes the only way.

| Concern | Pattern | Where it lives |
|---|---|---|
| Convention enforcement | baseline list + staleness test | `tests/Architecture/ConventionContractTests.cs` |
| Endpoint governance | one convention per `MapGroup`, verb-gated writes, conformance test per host | `AuthorizationContractTests.cs` (to be shared, S3) |
| Data access | primary constructor, `conn`, `QueryPagedAsync`, `AddNullable` | `docs/conventions.md` |
| Validation | FluentValidation at the boundary via `ExecuteAsync`; service validators for cross-entity rules | `docs/validation.md` |
| Errors | `ProblemResults` + `ApiErrorCodes`; `AppExceptionHandler` owns mapping | `docs/conventions.md` |
| Product composition | foundation `Add*`/`Use*`/`Map*` helpers; products call them in the same order | both `Program.cs` |
| Dialogs | `FormDialog`, `ConfirmDialog`, named exemptions | `docs/dialog-feedback.md` |
| Mutation feedback | `meta: { successMessage, invalidates }`; lint-enforced | `lib/core/query-client.ts`, `eslint.config.js:41-54` |
| Query keys | `qk` | `lib/api/query-keys.ts` |
| Lists | `OrkyoDataTable` with `EmptyState`, skeleton rows, `onRetry` | `components/ui/OrkyoDataTable.tsx` |
| API modules | `createCrudApi` for CRUD domains | `lib/api/create-crud-api.ts` (8 of 46 modules) |
| Product extension | render-prop slots on `TenantApp` | `orkyo-saas/frontend/src/App.tsx` |
| CI | reusable workflows in foundation, one SHA pin per consumer | `.github/workflows/reusable-*.yml` |
| Local hooks | pre-commit with `pre-commit`, `commit-msg`, `pre-push` install types | `orkyo-saas/setup.sh:56-62` |
| Docs numbers | the doc cites the test that owns the number | `AuthorizationContractTests`, `ToolNames.Writes` |

---

## 9. Phased roadmap

| Phase | Scope | Risk | Findings |
|---|---|---|---|
| 0 — prose and scripts | fix the sixteen stale statements; fix `setup.sh` and the pre-commit install types in foundation and community; delete `orkyo-community/.githooks`; merge the two UI guideline files; move completed plans | none | D1, X1, F12, X5 |
| 1 — fail-closed and conformance | `AllowTenantHeader` false in the image; shared authorization conformance helper run in SaaS and Community; typed tenantless `OrgContext`; scrub infra `.claude/settings.json`; calendar-feed scope decision | low | S1, S3, S4, S5, S2 |
| 2 — ratchets | baseline tests for services-with-SQL, `conn`/`db`, bare limits, bare `NotFound`, `Fetch/Load/Find`; ESLint rules for hand-rolled empty/loading markup; dead-dependency check | none (tests only) | B5, B6, F3, F9 |
| 3 — composition helpers | worker host, Valkey registration, Serilog envelope, `OrgContext` registration, `MigrationCli` options, migration scope field | medium (additive API + two downstream PRs) | B1, B2, B3 |
| 4 — CI consolidation | `reusable-product-ci.yml`; foundation consumes its own reusables; single pin per repo; per-purpose tokens | medium | X2, X4 |
| 5 — frontend convergence on touch | slot API for Community; `TimeAxisHeader`; DTO home + schema snapshot; pagination decisions; `RequestsPage` onto `meta`; store split; `utils` barrel; redirect retirement | low each, spread over feature work | F1, F4–F8, F10, F2 |

Phases 0–2 change no production behaviour except the one JSON default in Phase 1.

---

## 10. First safe PR

**Title:** `docs: correct the agent-facing contracts and install the hooks they describe`

One PR per repo, no runtime code:

- **orkyo-foundation:** `CLAUDE.md:20,165` (pre-commit, not `.githooks`), `:92` (historical plan);
  `README.md:61-71` (real tree); `setup.sh:20` (clear `core.hooksPath`, `pre-commit install
  --install-hooks`, copied from saas); `.pre-commit-config.yaml:8` (add `pre-push`);
  `docs/conventions.md:150` and the `NotFound` sentence; `docs/authorization.md:80,101,155-158`;
  `frontend/ARCHITECTURE.md` (imports, bridge, `App.tsx`, routes); `frontend/docs/UI-GUIDELINES.md:222`.
- **orkyo-community:** `setup.sh:38`; `.pre-commit-config.yaml:8`; delete `.githooks/`; `CLAUDE.md:57`.
- **orkyo-saas:** `CLAUDE.md:59`; `docs/architecture.md:31,596`.
- **orkyo-infra:** `docs/00-overview.md:62-76`; `structural-hardening-2026-05.md` path notes;
  `.claude/settings.json` scrub (S5).
- **orkyo-documentation:** `CLAUDE.md:7`.

Why this one first: it is zero-risk, it closes the one finding still open from July, and it turns the
docs-impact hook back on in the two repos where it is off. Every later phase is reviewed by people who
read these files first.

Validation: after `./setup.sh`, a commit touching `backend/src/Endpoints` without `Docs-impact:` is
rejected in all three product repos.

---

## 11. Backlog-ready tickets

| # | Repo | Title | Finding | Size |
|---|---|---|---|---|
| 1 | foundation, community | Install pre-commit correctly; remove `core.hooksPath` and `.githooks` | X1 | S |
| 2 | all | Correct the sixteen stale documentation statements | D1 | S |
| 3 | saas | Ship `AllowTenantHeader: false`; enable it for local dev only | S1 | S |
| 4 | foundation, saas, community | Shared authorization conformance helper; run it in each product host | S3 | M |
| 5 | foundation, saas | Typed tenantless `OrgContext`; one registration helper | S4 | M |
| 6 | infra | Scrub `.claude/settings.json`; move the checksum repair to a runbook | S5 | S |
| 7 | foundation | Decide and document the calendar-feed token scope; filter by user if chosen | S2 | S–M |
| 8 | foundation | Ratchet tests: services with SQL, `conn`/`db`, bare limits, bare `NotFound`, read verbs | B5, B6 | S |
| 9 | foundation | ESLint rules for hand-rolled empty/loading markup, with disable comments at current sites | F3 | S |
| 10 | foundation | `OrkyoWorkerHost` + `AddOrkyoValkey` + `RunHostAsync`; adopt in both products | B1 | M |
| 11 | foundation, community | `MigrationCli` takes configuration; Community migrator shrinks | B2 | S |
| 12 | foundation, community | Migration scope field replaces the `feedback` substring | B3 | S |
| 13 | foundation | Decide the fate of `migrations-foundation/revert/` | B7 | S |
| 14 | foundation, saas, community | `reusable-product-ci.yml`; foundation consumes its own reusables; one pin per repo | X2 | M |
| 15 | all | Per-purpose GitHub tokens instead of one `GHCR_TOKEN` | X4 | M |
| 16 | saas, community | Remove `@radix-ui/react-label` and `react-separator`; extend dedupe check to unused deps | F9 | S |
| 17 | foundation | Merge the two `UI-GUIDELINES.md`; fix the status palette | F12 | S |
| 18 | foundation, community | Community admin page via the `TenantApp` slot API | F1 | S |
| 19 | foundation | `TimeAxisHeader` shared by the three grids | F5 | M |
| 20 | foundation | One DTO home; OpenAPI property-name snapshot test for the top ten DTOs | F6 | M |
| 21 | foundation | Pagination decision per list; resource instances first | F4 | M |
| 22 | foundation | `RequestsPage` and `AiAssistantSettings` onto `useMutation` + `meta` | F8 | S |
| 23 | foundation | Split `app-store`; export `ui-actions-store`; retire `invalidate-request-data` | F7 | S |
| 24 | foundation | One `utils` barrel; `formatters.ts` under `lib/utils/` | F10 | S |
| 25 | foundation | Retirement version on the 33 legacy redirects | F2 | S |
| 26 | infra | `docs/plans/` with a status line per file; archive completed plans | X5 | S |
| 27 | foundation | Extract `RequestTreeRepository` on the next request-tree feature | B4 | M |
| 28 | foundation | Unit tests for `ChecksumPolicy` and `MigrationOrderer` | B8 | S |
| 29 | infra | Extend the no-silent-defaults linter to `infra/deploy/**` with markers (needs approval) | S6 | S |
| 30 | foundation | One smoke e2e per top-level nav item | F11 | M |
