# Remediation plan for the September 2026 design review

> **Status:** proposed, 2026-09-15. Companion to [`design-review-2026-09.md`](design-review-2026-09.md).
> The review is audit-only. This plan turns its 33 findings into 24 work packages, each the size of
> one pull request, in five waves. Nothing here is started.

## 1. How the plan is built

**Ground rules taken from the repos.** Every package respects these. None is repeated per package.

- **Audit-first is over. Each package is one PR.** A package lists the repo, the branch name, the
  findings it closes, the files, the checks that prove it, and what it waits for.
- **Foundation first, then bump, then products.** A product PR never references a foundation API
  that is not in its pinned package version (`orkyo-saas/CLAUDE.md`, `orkyo-community/CLAUDE.md`).
  Packages that add a foundation API are split: the foundation half lands, the nightly bump lands on
  product `main`, then the product half lands.
- **Docs-impact trailer** on every commit that touches endpoints, models, components or pages.
  `docs:`, `ci:`, `test:`, `build:` commits are exempt.
- **80% patch coverage**, measured locally with `scripts/ci/patch-coverage.sh` in foundation and the
  `dotnet test --collect` / `vitest --coverage` pair in the products.
- **Applied migrations are immutable.** No package edits a file under any `sql/` tree.
- **Infra approvals.** `deploy.yml`'s release-identity, migration-gate and rollback logic, and any new
  running service, need explicit approval (`orkyo-infra/CLAUDE.md`). Two packages touch that line
  and say so.
- **Documentation register.** Files under `docs/` and `frontend/docs/` follow the STE rules. The
  hook reports, it does not block.

**Sequencing principle.** Zero-risk prose and scripts first (Wave 0). Then tests that make later
changes safe (Wave 1). Then behaviour changes behind those tests (Wave 2). Then the shared-code
extractions that need a version bump (Wave 3). Then CI consolidation (Wave 4). Frontend convergence
rides feature work and has no wave of its own (§8).

**Size.** S = under a day, one reviewer pass. M = one to three days. L = more than three days or two
repos in lockstep.

## 2. Decisions the owner makes before Wave 2

Each decision has a default. A package that depends on a decision names it.

| # | Decision | Default | Why the default | Used by |
|---|---|---|---|---|
| D-A | Calendar-feed token scope: document "site or tenant", or filter to the token owner's assignments | Filter to the owner. The token table already stores `user_id`; the comment already promises it | S2 | WP-09 |
| D-B | Fate of `backend/migrations-foundation/revert/` (23 files, no consumer) | Delete, and record in `orkyo-infra/docs/migrations/classification.md` that rollback is snapshot-based | B7 | WP-08 |
| D-C | One `GHCR_TOKEN` or per-purpose tokens | Per-purpose: one for GHCR pulls, one for cross-repo dispatch, one for the ruleset-bypass push. Three secrets, three rotations | X4 | WP-22 |
| D-D | Pagination or virtualization per unpaged list | Paged for admin-shaped lists (users, sites, criteria, templates, resource types); virtualized for resource instances | F4 | WP-24 |
| D-E | Extend the no-silent-defaults linter into `infra/deploy/**` | Yes, in one approved PR that adds markers to the deliberate fallbacks first | S6 | WP-23 |

## 3. Wave 0 — prose and scripts (no runtime change)

### WP-01 · Correct the agent-facing contracts in foundation

Repo: orkyo-foundation · Branch: `docs/design-review-contracts` · Closes: D1 items 1, 3, 4–7, 10–13;
S7. F12 · Size: S · Risk: none

Changes:
- `CLAUDE.md:20` names pre-commit, not `.githooks/pre-push`; `:165` is deleted; `:92` marks the
  hardening plan historical and points at `orkyo-infra/docs/00-overview.md`.
- `README.md:61-71` shows the real tree (`core/`, `migration-abstractions/`, `migrator-runtime/`,
  `migrations-foundation/`, `seeding/`, `testsupport/`, `frontend/docs/`, `frontend/e2e/`).
- `frontend/ARCHITECTURE.md`: the import form is `@kymr10n/foundation/src/*`. The bridge module
  paragraph is removed. The `App.tsx` example moves to "how a product composes the shell". The route
  list names `ResourceClassPage`. The extension section describes the slot API only.
- `docs/authorization.md:80` cites `ToolNames.Writes` and `McpEndpointsTests` for the tool count;
  `:101` says "tools without `readOnlyHint`"; `:155-158` becomes a note that the gate is
  `AutoScheduleService.cs:174` and SaaS migration 2290.
- `docs/conventions.md:150` cites the new services-with-SQL baseline (WP-11) instead of a number;
  the bare-`NotFound` sentence lists the four AI/account sites as open.
- `docs/UI-GUIDELINES.md` becomes a two-line pointer. Its primitives section moves into
  `frontend/docs/UI-GUIDELINES.md` under a new heading. Line 222 lists the real status enum.

Checks: every path named in the changed files exists (`grep -o '\`[a-zA-Z./_-]*\`' | xargs -I{} test -e {}`);
STE hook output read; `docs:` commit.

### WP-02 · Install the hooks the contracts describe

Repo: orkyo-foundation and orkyo-community (one PR each) · Branch: `build/pre-commit-install` ·
Closes: X1, D1 item 1 (script half) · Size: S · Risk: none

Changes:
- `setup.sh`: the saas block (`orkyo-saas/setup.sh:55-67`) replaces the `core.hooksPath` line in
  foundation (`:20`) and community (`:38`).
- `.pre-commit-config.yaml:8` in both: `default_install_hook_types: [pre-commit, commit-msg, pre-push]`.
- Community: `.githooks/` deleted.
- Both products and foundation: `.github/dependabot.yml` gains `commit-message: { prefix: "chore(deps)" }`
  per ecosystem, so group bumps carry an exempt prefix. (The `commit-msg` hook never runs on
  Dependabot's commits. This fixes the audit signal, not a bypass.)

Checks: after `./setup.sh`, a staged change under `backend/src/Endpoints` with a `feat:` subject and
no trailer is rejected. A `git push` runs `dotnet format --verify-no-changes`; `git config --get
core.hooksPath` is empty.

### WP-03 · Correct the product and infra documentation

Repo: orkyo-saas, orkyo-infra, orkyo-documentation (one PR each) · Branch: `docs/design-review-contracts` ·
Closes: D1 items 2, 8, 9, 14, 15, 16. X5 · Size: S · Risk: none

Changes:
- `orkyo-saas/CLAUDE.md:59`, `orkyo-community/CLAUDE.md:57`: the hardening plan is historical.
- `orkyo-saas/docs/architecture.md:31` says .NET 10; `:596` says both editions wire observability;
  the `backend/src` folder list matches the tree. The AI, MCP, Insights and Lists endpoint families
  are listed.
- `orkyo-infra/docs/00-overview.md:62-76` shows `compose/base/`, `compose/nginx/`, `infra/`,
  `infra/deploy/`, and the real content of `compose/keycloak/` and `compose/{prod,staging}/`.
- `orkyo-infra/docs/structural-hardening-2026-05.md`: a header note lists the four paths that no
  longer exist and where each moved.
- `orkyo-infra/docs/plans/` is created; `optimization-plan-2026-07.md`, `-07b.md`,
  `remediation-plan-2026-07.md`, `optimization-review-2026-07-16.md`, `structural-hardening-2026-05.md`
  and the root `release-log-20260902.md` move there. Each file's first line is `Status: …`.
  `monitoring-upgrade-plan`, `pagespeed-improvement-plan` and `turnstile-plan` stay in `docs/` until
  their open rounds ship, with the same first line.
- `orkyo-documentation/CLAUDE.md:7` says Node `>=24`.
- `orkyo-foundation/requirements/`: the three non-archived packs get a `STATUS.md` (active, or
  archived with a completion date and a row in `COMPLETIONS_INDEX.md`).

Checks: `grep -rn "structural-hardening" */CLAUDE.md` shows "historical" on every hit. Every moved
file is reachable from `orkyo-infra/docs/00-overview.md` or `plans/README.md`.

### WP-04 · Scrub infra's Claude settings

Repo: orkyo-infra · Branch: `chore/claude-settings-scrub` · Closes: S5 · Size: S · Risk: none

Changes: `.claude/settings.json` keeps generic read-only patterns (`git *`, `ls`, `cat`, `grep`,
`docker compose config`). The SSH entry with the production IP and the `UPDATE
orkyo_schema_migrations` statement is removed. The checksum repair becomes
`docs/runbooks/migration-checksum-repair.md`: when it applies, what it changes, and that it needs the
same approval as any migration-gate change. The `/home/alex/...` entries are removed.

Checks: `grep -E '[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+|/home/|UPDATE ' .claude/settings.json` is empty.

## 4. Wave 1 — tests that make the later waves safe

### WP-05 · Authorization conformance for every host

Repo: orkyo-foundation, then orkyo-saas and orkyo-community · Branch: `test/authorization-contract-shared` ·
Closes: S3 · Size: M · Risk: none (tests only) · Waits for: nothing. Product halves wait for the bump

Changes:
- Foundation: `backend/testsupport/AuthorizationContract.cs` with
  `AssertEveryMutatingRouteIsGoverned(EndpointDataSource, IReadOnlyList<string> selfServicePrefixes)`.
  The body is the current `AuthorizationContractTests.cs:46-75`. The foundation test calls it.
  `Orkyo.Foundation.TestSupport.csproj` gains `Microsoft.AspNetCore.Routing` (it already references
  core. The Web assembly is not required for `EndpointDataSource`).
- SaaS: `backend/tests/Architecture/AuthorizationContractTests.cs` over `WebApplicationFactory<Program>`,
  with the foundation prefixes plus `/api/auth/demo`, `/api/interest`, `/api/fit-check`,
  `/api/design-partner`. Community: the same file with the foundation prefixes.
- The test fails first: one `RequireSiteAdmin()` removed from `TenantAdminEndpoints.cs` on a scratch
  branch turns it red. That run is linked in the PR.

Checks: `dotnet test --filter AuthorizationContract` green in all three; `scripts/test-downstream.sh`.

### WP-06 · Convention ratchets for the backend

Repo: orkyo-foundation · Branch: `test/convention-ratchets` · Closes: B5, B6 · Size: S · Risk: none

Changes: five new facts in `backend/tests/Architecture/ConventionContractTests.cs`, each in the
pattern of the SQL-literal baseline (`:166-188`): a file list, an assertion that no file outside
the list matches, and a staleness assertion that every listed file still matches.

| Fact | Pattern | Baseline size today |
|---|---|---|
| services that construct `NpgsqlCommand` | `new\s+NpgsqlCommand` under `core/Services` | 16 files |
| `db` connection locals | `await using var db = ` | 134 sites; the fact counts per file |
| bare `MaximumLength(<digits>)` in validators | regex | 44 sites |
| bare `Results.NotFound()` outside `CalendarFeedEndpoints.cs` | regex | 4 sites |
| `Fetch*`/`Load*`/`Find*Async` public methods | regex | ~14 methods |

`ICalendarWriter` is renamed to `CalendarWriter` in the same PR (one class, one call site at
`CalendarFeedEndpoints.cs:74`).

Checks: all five facts green on the branch. Each turns red when one new match is added on a
scratch commit.

### WP-07 · Frontend lint rules for empty and loading markup

Repo: orkyo-foundation · Branch: `test/ui-primitive-lint` · Closes: F3 (enforcement half) · Size: S · Risk: none

Changes: two `no-restricted-syntax` entries beside `banMutationCallbackFeedback`
(`frontend/eslint.config.js:41-54`):
- a JSX `className` literal containing both `text-center` and `text-muted-foreground` outside
  `components/ui/` → "use `EmptyState`";
- `JSXOpeningElement[name.name='Loader2']` outside `components/ui/` → "use `LoadingSpinner` or `Skeleton`".

Every current site gets `// eslint-disable-next-line no-restricted-syntax -- F3: converge on touch`.
The count only goes down from here.

Checks: `npm run lint` green. Removing one disable comment turns it red.

### WP-08 · Migration runtime tests and the revert tree

Repo: orkyo-foundation · Branch: `test/migrator-runtime` · Closes: B7, B8 · Size: S · Risk: low ·
Waits for: D-B

Changes: `backend/tests/Migrations/ChecksumPolicyTests.cs` and `MigrationOrdererTests.cs` (pure
unit tests. No container). Per D-B, `backend/migrations-foundation/revert/` is deleted and
`orkyo-infra/docs/migrations/classification.md` gains one paragraph on snapshot-based rollback. If
D-B is "keep", the package instead adds a runner in `migrator-runtime` and a runbook step, and the
size becomes M.

Checks: `dotnet test --filter Migrations`; `lint-migration-headers.sh` unaffected (the `revert/`
tree is outside `sql/`).

## 5. Wave 2 — behaviour changes behind the new tests

### WP-09 · Calendar-feed token scope

Repo: orkyo-foundation · Branch: `fix/calendar-feed-token-scope` · Closes: S2 · Size: S–M ·
Risk: low · Waits for: D-A

Changes (default D-A): `ICalendarFeedService.GetEventsAsync(Guid? siteId, Guid userId, …)` returns
the requests where the user holds an assignment; `CalendarFeedEndpoints.cs:70` passes
`stored.UserId`. The comment at `:47-50` stays true. A test: a token minted by user A returns no
event assigned only to user B. `Docs-impact:` names the calendar-feed user-guide page in
orkyo-documentation.

If D-A is "document": the comment and the user-guide page say "every scheduled request of the
site, or of the whole organisation when no site is chosen".

Checks: `dotnet test --filter CalendarFeed`. Patch coverage over 80%.

### WP-10 · `AllowTenantHeader` false in the image

Repo: orkyo-saas · Branch: `fix/tenant-header-default` · Closes: S1 · Size: S · Risk: low

Changes: `backend/api/appsettings.json:16` becomes `false`; `backend/api/appsettings.Development.json`
sets `true`; `dev.sh` exports `TenantResolution__AllowTenantHeader=true` for the host API. The
infra compose lines stay as a second guard. `ConfigurationValidator` is unchanged.

Checks: `CrossTenantIsolationTests` green; `./dev.sh api` still resolves a tenant by `X-Tenant-Slug`;
a plain `docker run` of the API image with no override rejects the header.

### WP-11 · Typed tenantless `OrgContext`

Repo: orkyo-foundation, then orkyo-saas · Branch: `fix/org-context-registration` · Closes: S4 ·
Size: M · Risk: medium · Waits for: WP-05 (the conformance net), then the bump

Changes:
- Foundation core: `TenantContextUnavailableException` and
  `AddOrgContextFromTenantContext()` in `backend/src/Configuration/`, which registers the scoped
  `OrgContext` factory once: `TenantContext` present → `OrgContextExtensions.FromTenant`. Absent →
  throw. `TenantSettingsService.cs:43` takes an explicit `ISiteContext` (or a nullable
  `IOrgContextAccessor`) instead of testing `OrgId == Guid.Empty`.
- SaaS: `backend/api/Program.cs:103-111` calls the helper. The sentinel is gone. Community keeps its
  options-based registration or calls the same helper with its `SingleTenantMiddleware` output.
- Test: a `[SkipTenantResolution]` route that resolves a tenant repository gets the typed
  exception, mapped by `AppExceptionHandler` to a 500 with a `code`.

Checks: `scripts/test-downstream.sh`. SaaS `ExplicitRegistrationTests` and `CompositionRootTests` green.

### WP-12 · Frontend feedback outliers

Repo: orkyo-foundation · Branch: `fix/feedback-outliers` · Closes: F8, part of F7 · Size: S · Risk: low

Changes: `components/settings/AiAssistantSettings.tsx` moves its 11 toasts onto `useMutation` +
`meta`; `pages/RequestsPage.tsx:318-352` replaces the hand-rolled toasts and `loadRequests()` with
`meta.invalidates`, and `docs/dialog-feedback.md:74-76` drops the exception;
`components/resources/ResourceScheduleDialog.tsx:129` uses `qk.resources.assignments(resourceId)`.
The seven form-shaped raw dialogs are each either converted or added to the exemption list with a
reason. The PR description lists the decision per file.

Checks: `vitest --coverage` over 80% on the touched files; `npm run lint` (the existing toast rule
now covers `AiAssistantSettings`).

## 6. Wave 3 — shared code that needs a version bump

Each package here is two PRs: the foundation half (additive API, CHANGELOG entry under
`[Unreleased]`), then the product half after the nightly bump lands on product `main`.

### WP-13 · Shared worker loop

Repo: orkyo-foundation → orkyo-saas, orkyo-community · Branch: `feat/foundation-worker-loop` ·
Closes: B1 (loop half) · Size: M · Risk: medium

Changes:
- Foundation core: `WorkerJob(string Name, Func<DateTimeOffset, bool> IsDue, Func<CancellationToken, Task> Run)`
  and `FoundationWorkerLoop : BackgroundService` that takes `IReadOnlyList<WorkerJob>` and owns the
  loop from `orkyo-saas/backend/worker/Program.cs:95-159` (jitter, `WorkerSchedulePolicy` delays,
  `HeldElsewhere` handling, the journal comment). Registered by
  `AddFoundationWorkerLoop(params WorkerJob[])` beside `AddFoundationWorkerServices`
  (`core/Configuration/FoundationWorkerServiceExtensions.cs:19`). Core gains
  `Microsoft.Extensions.Hosting.Abstractions` if it does not already have it.
- SaaS worker: three `WorkerJob`s (tenant lifecycle, user lifecycle, announcement broadcast);
  `WorkerService` deleted. Community worker: two jobs; `CommunityWorkerService` deleted.
- The Serilog bootstrap stays per host, per the recorded decision at
  `orkyo-saas/backend/worker/Program.cs:21-23` (core does not take a Serilog dependency).

Checks: a foundation unit test drives the loop with a fake `IWorkerJobCoordinator` and asserts each
job runs when due and skips when held. Product `SmokeTests` boot the worker host.

### WP-14 · Shared Valkey registration

Repo: orkyo-foundation → both products · Branch: `feat/foundation-valkey` · Closes: B1 (Valkey half) ·
Size: S · Risk: low

Changes: `AddOrkyoValkey(this IServiceCollection, IConfiguration)` in foundation `src/Configuration/`
registers `IConnectionMultiplexer` from `ConfigKeys.ValkeyConnection` with the existing exception
text. SaaS `Program.cs:155-157` and Community `:78-80` call it. Each keeps its own
`IBreakGlassSessionStore` line (the one line that differs). The explicit-registration rule holds: the
`Use*` for Valkey-backed middleware stays in `Program.cs`.

Checks: `ExplicitRegistrationTests` in both products. A foundation test that a missing key throws
at registration.

### WP-15 · `MigrationCli` takes configuration

Repo: orkyo-foundation → orkyo-community · Branch: `feat/migration-cli-options` · Closes: B2 ·
Size: S · Risk: low

Changes: `MigrationCli.RunAsync(string[] args, MigrationCliOptions options)` with
`ControlPlaneConnectionString` on the options. The existing env-var read becomes the default
`MigrationCliOptions.FromEnvironment()`. Community `backend/migrator/Program.cs:12-36` passes the
single connection string and drops the four duplicated constants and the `SetEnvironmentVariable`
calls. SaaS is unchanged (it uses the default).

Checks: `saas tests/Migrations/MigratorEndToEndTests.cs`. Community
`CommunityFoundationMigrationModuleTests`. A foundation unit test for `FromEnvironment()`.

### WP-16 · Migration scope replaces the filename substring

Repo: orkyo-foundation → orkyo-community · Branch: `feat/migration-scope` · Closes: B3 · Size: S ·
Risk: low

Changes: `MigrationScript` gains `MigrationScope Scope` (`Default`, `TenantDatabaseOnly`), read by
`EmbeddedSqlLoader` from a `-- @scope: tenant-database-only` header line (same mechanism as
`@migration-class`). Foundation adds the header to a **new** follow-up migration only where needed;
the two existing feedback migrations (1240, 1630) cannot be edited, so the abstractions carry an
explicit `KnownTenantOnlyIds` list for them, asserted against foundation's set by a test.
`CommunityFoundationMigrationModule.cs:26-31` filters on `Scope` and the list, not on `"feedback"`.
`lint-migration-headers.sh` accepts the new header.

Checks: `MigrationAbstractionsContractTests` extended. Community module test asserts every excluded
id exists upstream.

### WP-17 · Community admin page through the slot API

Repo: orkyo-community · Branch: `refactor/admin-slot` · Closes: F1 · Size: S · Risk: low ·
Waits for: WP-01 (ARCHITECTURE.md describes the slot API)

Changes: `orkyo-community/frontend/src/App.tsx:28-38` passes `renderAdminPage` to `TenantApp` as
SaaS does (`orkyo-saas/frontend/src/App.tsx:82`). The pathname interception is removed. No
foundation change: the slot already exists.

Checks: community frontend tests. The admin route still gates on `canAccessAdminPage`.

## 7. Wave 4 — CI and cross-repo

### WP-18 · Foundation consumes its own reusable workflows

Repo: orkyo-foundation · Branch: `ci/consume-own-reusables` · Closes: X2 (foundation half) ·
Size: S · Risk: low

Changes: `release-ci.yml` jobs `audit-secrets` (`:104`), `audit-nuget` (`:145`), `audit-npm` (`:173`)
and `container-scan` (`:402`) become `uses: ./.github/workflows/reusable-*.yml` with the same job
names; `security-refresh.yml` becomes a ten-line caller of `reusable-security-refresh.yml`.

Checks: the required-check names in branch protection are unchanged (job names kept). One green
run on the PR and one on `main`.

### WP-19 · One product CI workflow

Repo: orkyo-foundation → orkyo-saas, orkyo-community · Branch: `ci/reusable-product-ci` ·
Closes: X2 (product half) · Size: L · Risk: medium

Changes: `reusable-product-ci.yml` in foundation with inputs `product` (`saas`|`community`),
`api-port`, `keycloak-port`, and the jobs `detect-changes`, `backend-ci`, `frontend-ci`,
`smoke-tests`, `build-images`, `container-scan`. Each product's `release-ci.yml` keeps its own
`trigger-deploy`/`trigger-release`/`assemble-selfhosted`/`smoke-selfhosted`/`create-release` and
calls the reusable for the rest. Job names stay the ones branch protection lists today
(`orkyo-saas/.github/workflows/release-ci.yml:58-514`, community `:111-568`).

Checks: a PR per product whose only change is the swap. CI green with identical check names. The
diff of the removed YAML is reviewed against the reusable once.

### WP-20 · One reusable-workflow pin per repo

Repo: orkyo-saas, orkyo-community · Branch: `ci/align-reusable-pins` · Closes: X2 (pins) · Size: S ·
Risk: none

Changes: every `Kymr10n/orkyo-foundation/.github/workflows/reusable-*.yml@<sha>` reference in a repo
uses one SHA (the newest of the four in use today, `a367c81…` or later). Dependabot's
`github-actions` ecosystem keeps them moving together.

Checks: `grep -o 'reusable-[a-z-]*\.yml@[0-9a-f]*' .github/workflows/*.yml | cut -d@ -f2 | sort -u`
prints one line per repo.

### WP-21 · Local tooling parity

Repo: orkyo-saas, orkyo-community, orkyo-foundation · Branch: `chore/dev-parity` · Closes: X3 ·
Size: M · Risk: low

Changes: the shared `dev.sh` subcommands (`up`, `infra`, `api`, `worker`, `frontend`, `logs`,
`doctor`, `help`) move to `orkyo-foundation/scripts/dev-common.sh`, sourced by both product scripts;
the SaaS site-admin block and the connection-string exports stay product-side. `KC_DB` is `dev-file`
with a named volume in both `compose.local.yml`. Local images are pinned by tag **and** digest, the
same way infra pins them. A `scripts/ci/check-claude-dir.sh` in foundation diffs `.claude/` against
the sibling checkouts and is wired into `template-sync-check.yml`.

Checks: `./dev.sh doctor` in both products; `check-claude-dir.sh` exits 0.

### WP-22 · Per-purpose GitHub tokens

Repo: orkyo-infra, orkyo-foundation, orkyo-saas, orkyo-community · Branch: `ci/split-ghcr-token` ·
Closes: X4 (token row) · Size: M · Risk: medium · Waits for: D-C · **Needs approval:** touches
`deploy.yml` secrets

Changes (default D-C): `GHCR_PULL_TOKEN` (packages: read), `DISPATCH_TOKEN` (actions: write,
contents: read on the three product repos), `RELEASE_PUSH_TOKEN` (contents: write with ruleset
bypass on foundation, saas, community). The 16 workflow files reference the one their job needs.
`docs/deploy-env-vars.md` and `docs/runbooks/secret-rotation.md` list the three.

Checks: one full release train on staging with the new secrets before `GHCR_TOKEN` is revoked.

### WP-23 · No-silent-defaults linter over the deploy path

Repo: orkyo-infra · Branch: `ci/lint-defaults-deploy-path` · Closes: S6 · Size: S · Risk: low ·
Waits for: D-E · **Needs approval:** touches `deploy.yml` and `infra/deploy/**`

Changes: `lint-default-ok` markers on the deliberate fallbacks named in
`scripts/ci/lint-no-silent-defaults.sh:24-34` (`deploy.yml` `APPROVE_UNSAFE_MIGRATION` ternary,
`demo-reset.yml` schedule fallbacks). The scan scope gains `infra/deploy/**` and
`.github/workflows/**`.

Checks: `pr-checks.yml` lint job green. A scratch `${X:-y}` in `infra/deploy/` turns it red.

## 8. Frontend convergence — rides feature work, no wave

These are recorded as tickets and picked up by whichever feature next touches the file. Each is
small on its own. A sweep costs review attention and buys nothing.

| WP | Closes | Change | Trigger |
|---|---|---|---|
| WP-24 | F4 | Pagination or virtualization per list, per D-D; backend `PagedResult` where missing | next change to that list |
| WP-25 | F5 | `TimeAxisHeader` shared by `SchedulerGrid`, `TimelineGridShell`, `ResourceUtilizationGrid` | next grid change |
| WP-26 | F6 | DTOs move from `lib/api/*` to `types/<domain>.ts`; an OpenAPI property-name snapshot test for the top ten DTOs in `backend/tests/Architecture/` | next DTO change per domain |
| WP-27 | F7 | `app-store` → `timeline-store` + `chrome-store`; `ui-actions-store` exported; `invalidate-request-data` retired | next store change |
| WP-28 | F10 | one `lib/utils` barrel; `formatters.ts` under `lib/utils/` | any `lib/` change |
| WP-29 | F2 | retirement version on the 33 legacy redirects; removal in the next major | next `TenantApp` change |
| WP-30 | F9 | remove `@radix-ui/react-label`, `react-separator` from both products; `check-dedupe-sync.mjs` flags unused product deps | next dependency bump |
| WP-31 | F11 | one smoke e2e per top-level nav item | next e2e change |
| WP-32 | B4 | `RequestTreeRepository` extracted | next request-tree feature |

## 9. Dependency order

```
Wave 0   WP-01 ─┐        WP-02   WP-03   WP-04
                │
Wave 1   WP-05 ─┼─▶ (bump) ─▶ WP-05 product halves
         WP-06  │  WP-07   WP-08 (D-B)
                │
Wave 2   WP-09 (D-A)   WP-10   WP-11 (after WP-05 + bump)   WP-12
                │
Wave 3   WP-13 ─┼─▶ (bump) ─▶ product halves      WP-14, WP-15, WP-16 likewise
         WP-17 ◀┘ (after WP-01)
Wave 4   WP-18 ─▶ WP-19 ─▶ WP-20        WP-21        WP-22 (D-C, approval)   WP-23 (D-E, approval)
```

Waves 0 and 1 run in parallel across repos. Wave 2 starts when WP-05 is green in SaaS. Wave 3's
foundation halves start any time. Their product halves queue behind one nightly bump each. Wave 4
starts when WP-18 is green.

## 10. Effort and risk summary

| Wave | Packages | Size total | Runtime behaviour changed | Needs approval |
|---|---|---|---|---|
| 0 | 4 | 4 S | none | no |
| 1 | 4 | 3 S + 1 M | none | no |
| 2 | 4 | 3 S + 1 M | tenant-header default; feed scope; `OrgContext` failure mode; three UI files | no |
| 3 | 5 | 3 S + 2 M | worker loop; Valkey registration; migrator CLI; migration filter | no |
| 4 | 6 | 3 S + 2 M + 1 L | CI only; token split | WP-22, WP-23 |
| on touch | 9 | — | per feature | no |

Roughly six to eight working days for Waves 0–3, plus the Wave 4 CI consolidation, spread over the
normal review cadence. No package is a rewrite. The largest single diff is WP-19.

## 11. Finding-to-package map

| Finding | Package | Finding | Package | Finding | Package |
|---|---|---|---|---|---|
| S1 | WP-10 | B1 | WP-13, WP-14 | F1 | WP-17 |
| S2 | WP-09 | B2 | WP-15 | F2 | WP-29 |
| S3 | WP-05 | B3 | WP-16 | F3 | WP-07 (+ on touch) |
| S4 | WP-11 | B4 | WP-32 | F4 | WP-24 |
| S5 | WP-04 | B5 | WP-06 | F5 | WP-25 |
| S6 | WP-23 | B6 | WP-06 | F6 | WP-26 |
| S7 | WP-01 | B7 | WP-08 | F7 | WP-12, WP-27 |
| X1 | WP-02 | B8 | WP-08 | F8 | WP-12 |
| X2 | WP-18, WP-19, WP-20 | D1 | WP-01, WP-03 | F9 | WP-30 |
| X3 | WP-21 | | | F10 | WP-28 |
| X4 | WP-22 (token); rest is a register | | | F11 | WP-31 |
| X5 | WP-03 | | | F12 | WP-01 |

## 12. Not in this plan

- Merging the three time-grid engines or the two canvases (F5). The review marks them Defer with
  high migration risk. Only the header cell is shared.
- Replacing xstate, FullCalendar or react-day-picker. Each serves one component and works.
- Any change to `deploy.yml`'s release-identity, migration-gate or rollback logic beyond the two
  packages that name it.
- A CI coverage gate or a prose linter gate. Both are ruled out by the repos' own guides.
- Editing any applied migration.
