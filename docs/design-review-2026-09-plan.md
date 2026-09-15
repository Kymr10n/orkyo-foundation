# Remediation plan for the September 2026 design review

> **Status:** proposed, 2026-09-15, consolidated the same day. Companion to
> [`design-review-2026-09.md`](design-review-2026-09.md). The review is audit-only. This plan turns
> its 33 findings into **five pull requests, one per repository**. Each PR is a sequence of commits
> in risk order. The 23 work packages (WP-xx) from the first draft survive as those commits, so the
> review's finding map still resolves. Nine further items ride feature work and are not PRs.
> Nothing here is started.

## 1. How the plan is built

**Ground rules taken from the repos.** Every PR respects these; none is repeated per commit.

- **Foundation first, then bump, then products.** A product PR never references a foundation API
  that is not in its pinned package version (`orkyo-saas/CLAUDE.md`, `orkyo-community/CLAUDE.md`).
- **Docs-impact trailer** on every commit that touches endpoints, models, components or pages.
  `docs:`, `ci:`, `test:`, `build:` commits are exempt.
- **80% patch coverage**, measured locally with `scripts/ci/patch-coverage.sh` in foundation and the
  `dotnet test --collect` / `vitest --coverage` pair in the products.
- **Applied migrations are immutable.** No commit edits a file under any `sql/` tree.
- **Infra approvals.** `deploy.yml`'s release-identity, migration-gate and rollback logic, and any new
  running service, need explicit approval (`orkyo-infra/CLAUDE.md`). Two commits in PR-4 touch that
  line and say so.
- **Documentation register.** Files under `docs/` and `frontend/docs/` follow the STE rules. The
  hook reports, it does not block.

**What forces a PR boundary.**

1. A PR belongs to one repository. Five repositories change, so the floor is five PRs.
2. A product cannot consume a new foundation API until the nightly bump carries it. A product PR
   that opens after that bump carries every product change at once.
3. The infra approval rule is satisfied on the PR. It does not force a second PR.
4. A workflow change validates itself: GitHub runs the PR branch's workflow file on `pull_request`.
   The CI swap rides the same product PR as the code.

**What does not force a boundary.** Risk order. The first draft used five waves to spread risk over
many small PRs. Inside one PR the same order becomes the commit order. Reviewers review by commit,
and `git revert` keeps its granularity. No PR is added for sequencing.

**Size.** S = under a day, one reviewer pass. M = one to three days. L = more than three days.

## 2. Decisions the owner makes before PR-1 opens

Each decision has a default. A commit that depends on a decision names it.

| # | Decision | Default | Why the default | Used by |
|---|---|---|---|---|
| D-A | Calendar-feed token scope: document "site or tenant", or filter to the token owner's assignments | Filter to the owner. The token table already stores `user_id`. The comment already promises it | S2 | WP-09 |
| D-B | Fate of `backend/migrations-foundation/revert/` (23 files, no consumer) | Delete, and record in `orkyo-infra/docs/migrations/classification.md` that rollback is snapshot-based | B7 | WP-08 |
| D-C | One `GHCR_TOKEN` or per-purpose tokens | Per-purpose: one for GHCR pulls, one for cross-repo dispatch, one for the ruleset-bypass push. Three secrets, three rotations. The secrets exist before PR-1 opens | X4 | WP-22 |
| D-D | Pagination or virtualization per unpaged list | Paged for admin-shaped lists (users, sites, criteria, templates, resource types). Virtualized for resource instances | F4 | WP-24 |
| D-E | Extend the no-silent-defaults linter into `infra/deploy/**` | Yes, with markers on the deliberate fallbacks first | S6 | WP-23 |

## 3. PR-1 · orkyo-foundation

Branch: `claude/design-review-remediation` · 17 commits · Waits for: D-A, D-B, D-C (and the new
secrets if D-C is per-purpose) · Approval: none · Estimated diff: 3–5k lines, ~60 files

PR-1 is the large one. Three things keep it reviewable. Each commit is one former work package, in
the order below, with its own subject and `Docs-impact:` trailer. The PR description links a
scratch-branch run for each test commit that shows the test red before the fix. The whole diff runs
through `scripts/ci/patch-coverage.sh` once, and the number goes in the description. `CHANGELOG.md`
`[Unreleased]` gains one entry per new public API (commits 8, 10–13, 15).

### WP-01 · Correct the agent-facing contracts in foundation

Commit 1 of 17 · Closes: D1 items 1, 3, 4, 5, 7, 10–13; S7; F12 · Size: S · Risk: none

Changes:
- `CLAUDE.md:20` names pre-commit, not `.githooks/pre-push`. `:165` is deleted. `:92` marks the
  hardening plan historical and points at `orkyo-infra/docs/00-overview.md`.
- `README.md:61-71` shows the real tree (`core/`, `migration-abstractions/`, `migrator-runtime/`,
  `migrations-foundation/`, `seeding/`, `testsupport/`, `frontend/docs/`, `frontend/e2e/`).
- `frontend/ARCHITECTURE.md`: the import form is `@kymr10n/foundation/src/*`. The bridge module
  paragraph is removed. The `App.tsx` section says the products mirror foundation's `src/App.tsx`. The
  route list names `ResourceClassPage`. The extension section describes the slot API only.
- `docs/authorization.md:80` cites `ToolNames.Writes` and `McpEndpointsTests` for the tool count.
  `:101` says "tools without `readOnlyHint`". `:155-158` becomes a note that the gate is
  `AutoScheduleService.cs:174` and SaaS migration 2290.
- `docs/conventions.md:150` cites the services-with-SQL baseline from commit 3 instead of a number.
  The bare-`NotFound` sentence lists the four AI/account sites as open.
- `docs/UI-GUIDELINES.md` becomes a two-line pointer. Its primitives section moves into
  `frontend/docs/UI-GUIDELINES.md` under a new heading. Line 222 lists the real status enum.

Checks: every path named in the changed files exists. STE hook output read. `docs:` subject.

### WP-02 · Install the hooks the contracts describe

Commit 2 of 17 · Closes: X1 (foundation half) · Size: S · Risk: none

Changes:
- `setup.sh:20`: the saas block (`orkyo-saas/setup.sh:55-67`) replaces the `core.hooksPath` line.
- `.pre-commit-config.yaml:8`: `default_install_hook_types: [pre-commit, commit-msg, pre-push]`.
- `.github/dependabot.yml` gains `commit-message: { prefix: "chore(deps)" }` per ecosystem, so group
  bumps carry an exempt prefix. The `commit-msg` hook never runs on Dependabot's commits. This fixes
  the audit signal, not a bypass.

Checks: after `./setup.sh`, a staged change under `backend/src/Endpoints` with a `feat:` subject and
no trailer is rejected. `git push` runs `dotnet format --verify-no-changes`. `git config --get
core.hooksPath` is empty. Every later commit in this PR goes through the installed hook.

### WP-06 · Convention ratchets for the backend

Commit 3 of 17 · Closes: B5, B6 · Size: S · Risk: none

Changes: five new facts in `backend/tests/Architecture/ConventionContractTests.cs`, each in the
pattern of the SQL-literal baseline (`:166-188`): a file list, an assertion that no file outside
the list matches, and a staleness assertion that every listed file still matches.

| Fact | Pattern | Baseline size today |
|---|---|---|
| services that construct `NpgsqlCommand` | `new\s+NpgsqlCommand` under `core/Services` | 16 files |
| `db` connection locals | `await using var db = ` | 134 sites. The fact counts per file |
| bare `MaximumLength(<digits>)` in validators | regex | 44 sites |
| bare `Results.NotFound()` outside `CalendarFeedEndpoints.cs` | regex | 4 sites |
| `Fetch*`/`Load*`/`Find*Async` public methods | regex | ~14 methods |

`ICalendarWriter` is renamed to `CalendarWriter` in the same commit (one class, one call site at
`CalendarFeedEndpoints.cs:74`).

Checks: all five facts green. Each turns red when one new match is added on a scratch commit.

### WP-07 · Frontend lint rules for empty and loading markup

Commit 4 of 17 · Closes: F3 (enforcement half) · Size: S · Risk: none

Changes: two `no-restricted-syntax` entries beside `banMutationCallbackFeedback`
(`frontend/eslint.config.js:41-54`):
- a JSX `className` literal containing both `text-center` and `text-muted-foreground` outside
  `components/ui/` → "use `EmptyState`";
- `JSXOpeningElement[name.name='Loader2']` outside `components/ui/` → "use `LoadingSpinner` or `Skeleton`".

Every current site gets `// eslint-disable-next-line no-restricted-syntax -- F3: converge on touch`.
The count only goes down from here.

Checks: `npm run lint` green. Removing one disable comment turns it red.

### WP-08 · Migration runtime tests and the revert tree

Commit 5 of 17 · Closes: B7, B8 · Size: S · Risk: low · Waits for: D-B

Changes: `backend/tests/Migrations/ChecksumPolicyTests.cs` and `MigrationOrdererTests.cs` (pure
unit tests, no container). Per D-B, `backend/migrations-foundation/revert/` is deleted. The
classification.md paragraph lands in PR-4. If D-B is "keep", this commit instead adds a runner in
`migrator-runtime` and a runbook step, and the size becomes M.

Checks: `dotnet test --filter Migrations`. `lint-migration-headers.sh` is unaffected (the `revert/`
tree is outside `sql/`).

### WP-05 · Authorization conformance helper

Commit 6 of 17 · Closes: S3 (foundation half) · Size: S · Risk: none

Changes: `backend/testsupport/AuthorizationContract.cs` with
`AssertEveryMutatingRouteIsGoverned(EndpointDataSource, IReadOnlyList<string> selfServicePrefixes)`.
The body is the current `AuthorizationContractTests.cs:46-75`. The foundation test calls it.
`Orkyo.Foundation.TestSupport.csproj` gains `Microsoft.AspNetCore.Routing` (it already references
core; the Web assembly is not required for `EndpointDataSource`). The product tests land in PR-2 and
PR-3.

Checks: `dotnet test --filter AuthorizationContract` green.

### WP-09 · Calendar-feed token scope

Commit 7 of 17 · Closes: S2 · Size: S–M · Risk: low · Waits for: D-A

Changes (default D-A): `ICalendarFeedService.GetEventsAsync(Guid? siteId, Guid userId, …)` returns
the requests where the user holds an assignment. `CalendarFeedEndpoints.cs:70` passes
`stored.UserId`. The comment at `:47-50` stays true. A test: a token minted by user A returns no
event assigned only to user B. `Docs-impact:` names the calendar-feed user-guide page in
orkyo-documentation (PR-5).

If D-A is "document": the comment and the user-guide page say "every scheduled request of the
site, or of the whole organisation when no site is chosen".

Checks: `dotnet test --filter CalendarFeed`. Patch coverage over 80%.

### WP-11 · Typed tenantless `OrgContext`

Commit 8 of 17 · Closes: S4 (foundation half) · Size: M · Risk: medium

Changes: `TenantContextUnavailableException` and `AddOrgContextFromTenantContext()` in
`backend/src/Configuration/`, which registers the scoped `OrgContext` factory once: `TenantContext`
present → `OrgContextExtensions.FromTenant`; absent → throw. `TenantSettingsService.cs:43` takes an
explicit `ISiteContext` (or a nullable `IOrgContextAccessor`) instead of testing `OrgId ==
Guid.Empty`. `AppExceptionHandler` maps the new exception to a 500 with a `code`. A test: a
`[SkipTenantResolution]` route that resolves a tenant repository gets the typed exception. SaaS
adopts the helper in PR-2.

Checks: foundation tests green. CHANGELOG entry.

### WP-12 · Frontend feedback outliers

Commit 9 of 17 · Closes: F8, part of F7 · Size: S · Risk: low

Changes: `components/settings/AiAssistantSettings.tsx` moves its 11 toasts onto `useMutation` +
`meta`. `pages/RequestsPage.tsx:318-352` replaces the hand-rolled toasts and `loadRequests()` with
`meta.invalidates`, and `docs/dialog-feedback.md:74-76` drops the exception.
`components/resources/ResourceScheduleDialog.tsx:129` uses `qk.resources.assignments(resourceId)`.
The seven form-shaped raw dialogs are each either converted or added to the exemption list with a
reason. The commit body lists the decision per file.

Checks: `vitest --coverage` over 80% on the touched files. `npm run lint`.

### WP-13 · Shared worker loop

Commit 10 of 17 · Closes: B1 (loop half, foundation side) · Size: M · Risk: medium

Changes: `WorkerJob(string Name, Func<DateTimeOffset, bool> IsDue, Func<CancellationToken, Task> Run)`
and `FoundationWorkerLoop : BackgroundService` in core. It takes `IReadOnlyList<WorkerJob>` and owns
the loop from `orkyo-saas/backend/worker/Program.cs:95-159` (jitter, `WorkerSchedulePolicy` delays,
`HeldElsewhere` handling, the journal comment). Registered by
`AddFoundationWorkerLoop(params WorkerJob[])` beside `AddFoundationWorkerServices`
(`core/Configuration/FoundationWorkerServiceExtensions.cs:19`). Core gains
`Microsoft.Extensions.Hosting.Abstractions` if it does not already have it. The Serilog bootstrap
stays per host, per the recorded decision at `orkyo-saas/backend/worker/Program.cs:21-23`.

Checks: a unit test drives the loop with a fake `IWorkerJobCoordinator` and asserts each job runs
when due and skips when held. CHANGELOG entry.

### WP-14 · Shared Valkey registration

Commit 11 of 17 · Closes: B1 (Valkey half, foundation side) · Size: S · Risk: low

Changes: `AddOrkyoValkey(this IServiceCollection, IConfiguration)` in `src/Configuration/` registers
`IConnectionMultiplexer` from `ConfigKeys.ValkeyConnection` with the existing exception text. The
`IBreakGlassSessionStore` line stays product-side (the one line that differs).

Checks: a test that a missing key throws at registration. CHANGELOG entry.

### WP-15 · `MigrationCli` takes configuration

Commit 12 of 17 · Closes: B2 (foundation half) · Size: S · Risk: low

Changes: `MigrationCli.RunAsync(string[] args, MigrationCliOptions options)` with
`ControlPlaneConnectionString` on the options. The existing env-var read becomes the default
`MigrationCliOptions.FromEnvironment()`. SaaS is unchanged (it uses the default). Community adopts
it in PR-3.

Checks: a unit test for `FromEnvironment()`. CHANGELOG entry.

### WP-16 · Migration scope replaces the filename substring

Commit 13 of 17 · Closes: B3 (foundation half) · Size: S · Risk: low

Changes: `MigrationScript` gains `MigrationScope Scope` (`Default`, `TenantDatabaseOnly`), read by
`EmbeddedSqlLoader` from a `-- @scope: tenant-database-only` header line (same mechanism as
`@migration-class`). The two existing feedback migrations (1240, 1630) cannot be edited, so the
abstractions carry an explicit `KnownTenantOnlyIds` list for them, asserted against foundation's
set by a test. `lint-migration-headers.sh` accepts the new header. Community's filter changes in PR-3.

Checks: `MigrationAbstractionsContractTests` extended. CHANGELOG entry.

### WP-18 · Foundation consumes its own reusable workflows

Commit 14 of 17 · Closes: X2 (foundation half) · Size: S · Risk: low

Changes: `release-ci.yml` jobs `audit-secrets` (`:104`), `audit-nuget` (`:145`), `audit-npm` (`:173`)
and `container-scan` (`:402`) become `uses: ./.github/workflows/reusable-*.yml` with the same job
names. `security-refresh.yml` becomes a ten-line caller of `reusable-security-refresh.yml`.

Checks: the required-check names in branch protection are unchanged. The PR's own CI run is the proof.

### WP-19 · One product CI workflow (foundation half)

Commit 15 of 17 · Closes: X2 (product half, definition) · Size: M · Risk: low

Changes: `reusable-product-ci.yml` with inputs `product` (`saas`|`community`), `api-port`,
`keycloak-port`, and the jobs `detect-changes`, `backend-ci`, `frontend-ci`, `smoke-tests`,
`build-images`, `container-scan`, lifted from `orkyo-saas/.github/workflows/release-ci.yml:58-514`
and community `:111-568`. The products swap to it in PR-2 and PR-3.

Checks: `actionlint` on the file. The real proof is the product PRs' CI.

### WP-21 · Local tooling parity (foundation half)

Commit 16 of 17 · Closes: X3 (foundation half) · Size: S · Risk: none

Changes: `scripts/dev-common.sh` holds the shared `dev.sh` subcommands (`up`, `infra`, `api`,
`worker`, `frontend`, `logs`, `doctor`, `help`). `scripts/ci/check-claude-dir.sh` diffs `.claude/`
against the sibling checkouts and is wired into `template-sync-check.yml`.

Checks: `check-claude-dir.sh` exits 0 on the current tree.

### WP-22 · Per-purpose GitHub tokens (foundation half)

Commit 17 of 17 · Closes: X4 (token row, foundation side) · Size: S · Risk: medium · Waits for: D-C
and the secrets

Changes (default D-C): `release-ci.yml:742`, `advance-classification-baseline.yml:30`,
`template-sync-check.yml:48` and the reusable workflows reference `GHCR_PULL_TOKEN`,
`DISPATCH_TOKEN` or `RELEASE_PUSH_TOKEN` per job. `GHCR_TOKEN` stays defined until PR-4 completes
the rotation, so nothing breaks between merges.

Checks: one nightly publish on `main` after merge uses the new secrets.

## 4. PR-2 · orkyo-saas

Branch: `claude/design-review-remediation` · 11 commits · Waits for: PR-1 merged and the nightly
bump (`chore: bump foundation …`) on `main` · Approval: none · Estimated diff: ~1.2k lines

### WP-03 · Correct the SaaS documentation

Commit 1 of 11 · Closes: D1 items 2, 8, 9 · Size: S · Risk: none

Changes: `CLAUDE.md:59` marks the hardening plan historical. `docs/architecture.md:31` says .NET 10.
`:596` says both editions wire observability. The `backend/src` folder list matches the tree. The
AI, MCP, Insights and Lists endpoint families are listed.

### WP-02 · Dependabot commit prefix

Commit 2 of 11 · Closes: X1 (saas share) · Size: S · Risk: none

Changes: `.github/dependabot.yml` gains `commit-message: { prefix: "chore(deps)" }` per ecosystem.
`setup.sh` is already correct.

### WP-10 · `AllowTenantHeader` false in the image

Commit 3 of 11 · Closes: S1 · Size: S · Risk: low

Changes: `backend/api/appsettings.json:16` becomes `false`. `backend/api/appsettings.Development.json`
sets `true`. `dev.sh` exports `TenantResolution__AllowTenantHeader=true` for the host API. The infra
compose lines stay as a second guard. `ConfigurationValidator` is unchanged.

Checks: `CrossTenantIsolationTests` green. `./dev.sh api` still resolves a tenant by `X-Tenant-Slug`.
A plain `docker run` of the API image with no override rejects the header.

### WP-05 · SaaS authorization conformance test

Commit 4 of 11 · Closes: S3 (saas half) · Size: S · Risk: none

Changes: `backend/tests/Architecture/AuthorizationContractTests.cs` over
`WebApplicationFactory<Program>`, calling the testsupport helper with the foundation prefixes plus
`/api/auth/demo`, `/api/interest`, `/api/fit-check`, `/api/design-partner`. The PR description links
a scratch run where one `RequireSiteAdmin()` removed from `TenantAdminEndpoints.cs` turns it red.

### WP-11 · Use the `OrgContext` helper

Commit 5 of 11 · Closes: S4 (saas half) · Size: S · Risk: medium

Changes: `backend/api/Program.cs:103-111` calls `AddOrgContextFromTenantContext()`. The sentinel is
gone.

Checks: `ExplicitRegistrationTests`, `CompositionRootTests`, `CrossTenantIsolationTests`.

### WP-13 · Worker jobs

Commit 6 of 11 · Closes: B1 (loop half) · Size: S · Risk: medium

Changes: `backend/worker/Program.cs` registers three `WorkerJob`s (tenant lifecycle, user lifecycle,
announcement broadcast) through `AddFoundationWorkerLoop`. `WorkerService` (`:72-159`) is deleted.

Checks: `SmokeTests` boot the worker host.

### WP-14 · Valkey helper

Commit 7 of 11 · Closes: B1 (Valkey half) · Size: S · Risk: low

Changes: `backend/api/Program.cs:155-157` calls `AddOrkyoValkey`. The
`ValkeyBreakGlassSessionStore` line stays.

### WP-19 + WP-20 · Workflow swap and a single pin

Commit 8 of 11 · Closes: X2 (product half) · Size: M · Risk: medium

Changes: `.github/workflows/release-ci.yml:58-514` becomes one `uses:
Kymr10n/orkyo-foundation/.github/workflows/reusable-product-ci.yml@<sha>` call with `product: saas`.
`trigger-deploy`, `trigger-release`, `auto-bump` and `release-evidence-bundle` stay. Every
`reusable-*.yml@<sha>` reference in the repo uses the same SHA.

Checks: the PR's own CI run shows the same check names green. `grep -o 'reusable-[a-z-]*\.yml@[0-9a-f]*'
.github/workflows/*.yml | cut -d@ -f2 | sort -u` prints one line.

### WP-21 · Local tooling parity

Commit 9 of 11 · Closes: X3 (saas share) · Size: S · Risk: low

Changes: `dev.sh` sources foundation's `scripts/dev-common.sh` and keeps the site-admin block and
the connection-string exports. `compose.local.yml` pins images by tag and digest. `KC_DB` stays
`dev-file`.

Checks: `./dev.sh doctor`.

### WP-30 · Dead dependencies

Commit 10 of 11 · Closes: F9 (saas share) · Size: S · Risk: none

Changes: `frontend/package.json:44,48` drop `@radix-ui/react-label` and `@radix-ui/react-separator`.
`scripts/check-dedupe-sync.mjs` flags product dependencies that no source file imports.

### WP-22 · Token references

Commit 11 of 11 · Closes: X4 (saas share) · Size: S · Risk: low · Waits for: D-C

Changes: workflow jobs reference the per-purpose secret each needs.

## 5. PR-3 · orkyo-community

Branch: `claude/design-review-remediation` · 12 commits · Waits for: PR-1 merged and the nightly
bump on `main` · Approval: none · Estimated diff: ~900 lines

### WP-03 · Correct the Community documentation

Commit 1 of 12 · Closes: D1 item 2 · Size: S · Risk: none

Changes: `CLAUDE.md:57` marks the hardening plan historical.

### WP-02 · Install the hooks the contracts describe

Commit 2 of 12 · Closes: X1 (community half) · Size: S · Risk: none

Changes: `setup.sh:38` takes the saas block. `.pre-commit-config.yaml:8` adds `pre-push`.
`.githooks/` is deleted. `.github/dependabot.yml` gains the `chore(deps)` prefix.

Checks: after `./setup.sh`, an untrailed `feat:` commit under `backend/src` is rejected.

### WP-05 · Community authorization conformance test

Commit 3 of 12 · Closes: S3 (community half) · Size: S · Risk: none

Changes: `backend/tests/Architecture/AuthorizationContractTests.cs` over
`WebApplicationFactory<Program>` with the foundation prefixes.

### WP-13 · Worker jobs

Commit 4 of 12 · Closes: B1 (loop half) · Size: S · Risk: medium

Changes: `backend/worker/Program.cs` registers two `WorkerJob`s. `CommunityWorkerService`
(`:63-125`) is deleted.

### WP-14 · Valkey helper

Commit 5 of 12 · Closes: B1 (Valkey half) · Size: S · Risk: low

Changes: `backend/api/Program.cs:78-80` calls `AddOrkyoValkey`. The `NullBreakGlassSessionStore`
line stays.

### WP-15 · Migrator shrink

Commit 6 of 12 · Closes: B2 (community half) · Size: S · Risk: low

Changes: `backend/migrator/Program.cs:12-36` passes `MigrationCliOptions` with the single connection
string and drops the four duplicated constants and the `SetEnvironmentVariable` calls.

Checks: `CommunityFoundationMigrationModuleTests`. `SmokeTests`.

### WP-16 · Scope filter

Commit 7 of 12 · Closes: B3 (community half) · Size: S · Risk: low

Changes: `backend/migrations/CommunityFoundationMigrationModule.cs:26-31` filters on
`Scope == TenantDatabaseOnly` or `KnownTenantOnlyIds`, not on `"feedback"`.

Checks: the module test asserts every excluded id exists upstream.

### WP-17 · Admin page through the slot API

Commit 8 of 12 · Closes: F1 · Size: S · Risk: low

Changes: `frontend/src/App.tsx:28-38` passes `renderAdminPage` to `TenantApp` as SaaS does
(`orkyo-saas/frontend/src/App.tsx:82`). The pathname interception is removed.

Checks: frontend tests. The admin route still gates on `canAccessAdminPage`.

### WP-19 + WP-20 · Workflow swap and a single pin

Commit 9 of 12 · Closes: X2 (product half) · Size: M · Risk: medium

Changes: `.github/workflows/release-ci.yml:111-568` becomes the `reusable-product-ci.yml` call with
`product: community`. `nightly-change-check`, `dependency-review`, `assemble-selfhosted`,
`smoke-selfhosted`, `create-release`, `auto-bump` and `release-evidence-bundle` stay. One SHA for
every reusable reference.

### WP-21 · Local tooling parity

Commit 10 of 12 · Closes: X3 (community share) · Size: S · Risk: low

Changes: `dev.sh` sources `dev-common.sh`. `compose.local.yml:38` sets `KC_DB: dev-file` with a
named volume, and pins images by digest.

### WP-30 · Dead dependencies

Commit 11 of 12 · Closes: F9 (community share) · Size: S · Risk: none

Changes: `frontend/package.json:43,47` drop the two radix packages.

### WP-22 · Token references

Commit 12 of 12 · Closes: X4 (community share) · Size: S · Risk: low · Waits for: D-C

## 6. PR-4 · orkyo-infra

Branch: `claude/design-review-remediation` · 5 commits · Waits for: D-B, D-C, D-E · **Approval:
required** for commits 4 and 5 (they touch `deploy.yml` and `infra/deploy/**`) · Runs in parallel
with PR-1

If approval is slow, commits 1–3 are the peel-off: a docs-only PR that merges first (six PRs in
total).

### WP-04 · Scrub the Claude settings

Commit 1 of 5 · Closes: S5 · Size: S · Risk: none

Changes: `.claude/settings.json` keeps generic read-only patterns. The SSH entry with the production
IP and the `UPDATE orkyo_schema_migrations` statement is removed. The checksum repair becomes
`docs/runbooks/migration-checksum-repair.md`. The `/home/alex/...` entries are removed.

Checks: `grep -E '[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+|/home/|UPDATE ' .claude/settings.json` is empty.

### WP-03 · Correct the infra documentation and archive the plans

Commit 2 of 5 · Closes: D1 items 14, 15; X5 · Size: S · Risk: none

Changes: `docs/00-overview.md:62-76` shows `compose/base/`, `compose/nginx/`, `infra/`,
`infra/deploy/`, and the real content of `compose/keycloak/` and `compose/{prod,staging}/`.
`docs/structural-hardening-2026-05.md` gets a header note listing the four dead paths and where each
moved. `docs/plans/` is created. `optimization-plan-2026-07.md`, `-07b.md`,
`remediation-plan-2026-07.md`, `optimization-review-2026-07-16.md`, `structural-hardening-2026-05.md`
and the root `release-log-20260902.md` move there, each with `Status: …` as its first line.
`monitoring-upgrade-plan`, `pagespeed-improvement-plan` and `turnstile-plan` stay in `docs/` with
the same first line.

### WP-08 · Rollback is snapshot-based

Commit 3 of 5 · Closes: B7 (docs half) · Size: S · Risk: none · Waits for: D-B

Changes: `docs/migrations/classification.md` gains one paragraph: rollback is by Hetzner snapshot;
no revert scripts exist.

### WP-23 · No-silent-defaults linter over the deploy path

Commit 4 of 5 · Closes: S6 · Size: S · Risk: low · Waits for: D-E · **Needs approval**

Changes: `lint-default-ok` markers on the deliberate fallbacks named in
`scripts/ci/lint-no-silent-defaults.sh:24-34` (`deploy.yml` `APPROVE_UNSAFE_MIGRATION` ternary,
`demo-reset.yml` schedule fallbacks). The scan scope gains `infra/deploy/**` and
`.github/workflows/**`.

Checks: `pr-checks.yml` lint job green. A scratch `${X:-y}` in `infra/deploy/` turns it red.

### WP-22 · Per-purpose GitHub tokens

Commit 5 of 5 · Closes: X4 (token row) · Size: M · Risk: medium · Waits for: D-C · **Needs approval**

Changes (default D-C): `release.yml:41`, `pr-checks.yml:226`, `deploy.yml` and the composite actions
reference `GHCR_PULL_TOKEN`, `DISPATCH_TOKEN` or `RELEASE_PUSH_TOKEN` per job.
`docs/deploy-env-vars.md` and `docs/runbooks/secret-rotation.md` list the three. `GHCR_TOKEN` is
revoked only after one full release train on staging succeeds with the new secrets.

Checks: one staging release train.

## 7. PR-5 · orkyo-documentation

Branch: `claude/design-review-remediation` · 2 commits · Waits for: PR-1 merged (commit 2 describes
the feed behaviour that PR-1 ships) · Approval: none

### WP-03 · Node version

Commit 1 of 2 · Closes: D1 item 16 · Size: S · Risk: none

Changes: `CLAUDE.md:7` says Node `>=24`, matching `package.json:32` and `.nvmrc`.

### WP-09 · Calendar-feed page

Commit 2 of 2 · Closes: S2 (docs half) · Size: S · Risk: none · Waits for: D-A

Changes: the user-guide page for calendar subscriptions states the token's scope as shipped in PR-1
(the owner's assignments by default, or the site/organisation wording if D-A is "document").

Checks: `npm run build`. `npm run check:ste`.

## 8. Rides feature work — not a PR

These are tickets picked up by whichever feature next touches the file. Folding them into PR-1
turns it into the sweep the review argued against, so they stay out.

| WP | Closes | Change | Trigger |
|---|---|---|---|
| WP-24 | F4 | Pagination or virtualization per list, per D-D. Backend `PagedResult` where missing | next change to that list |
| WP-25 | F5 | `TimeAxisHeader` shared by `SchedulerGrid`, `TimelineGridShell`, `ResourceUtilizationGrid` | next grid change |
| WP-26 | F6 | DTOs move from `lib/api/*` to `types/<domain>.ts`. An OpenAPI property-name snapshot test for the top ten DTOs | next DTO change per domain |
| WP-27 | F7 | `app-store` → `timeline-store` + `chrome-store`. `ui-actions-store` exported. `invalidate-request-data` retired | next store change |
| WP-28 | F10 | one `lib/utils` barrel. `formatters.ts` under `lib/utils/` | any `lib/` change |
| WP-29 | F2 | retirement version on the 33 legacy redirects. Removal in the next major | next `TenantApp` change |
| WP-31 | F11 | one smoke e2e per top-level nav item | next e2e change |
| WP-32 | B4 | `RequestTreeRepository` extracted | next request-tree feature |

WP-30 (dead product dependencies) moved into PR-2 and PR-3: it is two `package.json` lines per
product and costs nothing to carry.

## 9. Dependency order

```
             ┌─ PR-4 infra (docs commits any time; commits 4–5 after approval)
             ├─ PR-5 documentation (commit 2 after PR-1 merges)
decisions ───┤
D-A…D-E,     └─ PR-1 foundation ──merge──▶ nightly bump (02:00 UTC) ──▶ PR-2 saas
secrets                                                              └─▶ PR-3 community
```

Critical path: PR-1 review, merge, one nightly bump, then PR-2 and PR-3 in parallel. PR-4 and PR-5
run beside PR-1.

## 10. Effort and risk summary

| PR | Commits | Est. diff | Runtime behaviour changed | Approval |
|---|---|---|---|---|
| PR-1 foundation | 17 | 3–5k lines, ~60 files | feed scope; `OrgContext` failure mode; three UI files; new APIs (additive) | no |
| PR-2 saas | 11 | ~1.2k lines | tenant-header default; worker host; `OrgContext` registration; CI swap | no |
| PR-3 community | 12 | ~900 lines | worker host; migrator CLI; migration filter; admin routing; CI swap | no |
| PR-4 infra | 5 | ~600 lines | token split; linter scope | commits 4–5 |
| PR-5 documentation | 2 | ~60 lines | none | no |

Roughly six to eight working days of change, plus one calendar day for the nightly bump between
PR-1 and the product PRs. The review load concentrates in PR-1. The commit-per-package structure is
what makes that load tractable.

## 11. Finding-to-PR map

| Finding | PR / commit | Finding | PR / commit | Finding | PR / commit |
|---|---|---|---|---|---|
| S1 | PR-2 / WP-10 | B1 | PR-1 / WP-13, WP-14 → PR-2, PR-3 | F1 | PR-3 / WP-17 |
| S2 | PR-1 / WP-09 → PR-5 | B2 | PR-1 / WP-15 → PR-3 | F2 | on touch / WP-29 |
| S3 | PR-1 / WP-05 → PR-2, PR-3 | B3 | PR-1 / WP-16 → PR-3 | F3 | PR-1 / WP-07 (+ on touch) |
| S4 | PR-1 / WP-11 → PR-2 | B4 | on touch / WP-32 | F4 | on touch / WP-24 |
| S5 | PR-4 / WP-04 | B5 | PR-1 / WP-06 | F5 | on touch / WP-25 |
| S6 | PR-4 / WP-23 | B6 | PR-1 / WP-06 | F6 | on touch / WP-26 |
| S7 | PR-1 / WP-01 | B7 | PR-1 / WP-08 → PR-4 | F7 | PR-1 / WP-12; on touch / WP-27 |
| X1 | PR-1, PR-2, PR-3 / WP-02 | B8 | PR-1 / WP-08 | F8 | PR-1 / WP-12 |
| X2 | PR-1 / WP-18, WP-19 → PR-2, PR-3 | D1 | PR-1 / WP-01; PR-2–5 / WP-03 | F9 | PR-2, PR-3 / WP-30 |
| X3 | PR-1 / WP-21 → PR-2, PR-3 | | | F10 | on touch / WP-28 |
| X4 | PR-1–4 / WP-22 (token); rest is a register | | | F11 | on touch / WP-31 |
| X5 | PR-4 / WP-03 | | | F12 | PR-1 / WP-01 |

## 12. Not in this plan

- Merging the three time-grid engines or the two canvases (F5). The review marks them Defer with
  high migration risk. Only the header cell is shared.
- Replacing xstate, FullCalendar or react-day-picker. Each serves one component and works.
- Any change to `deploy.yml`'s release-identity, migration-gate or rollback logic beyond the two
  commits that name it.
- A CI coverage gate or a prose linter gate. Both are ruled out by the repos' own guides.
- Editing any applied migration.
- A sixth PR. The one split worth making, if a 3–5k-line review is unwelcome or infra approval is
  slow: PR-1's first two commits (docs and hooks) as a same-day docs PR, or PR-4's first three
  commits likewise. Both are peel-offs of what is written here, not new work.
