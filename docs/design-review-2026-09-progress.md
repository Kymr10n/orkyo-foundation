# Progress on the September 2026 remediation

> **Status:** 2026-09-15, end of the first implementation session. Companion to
> [`design-review-2026-09.md`](design-review-2026-09.md) (the audit) and
> [`design-review-2026-09-plan.md`](design-review-2026-09-plan.md) (the five-PR plan).
> This file records what is done, what is open, and what the owner does next.

## 1. Summary

| PR | Repository | State | Commits | CI |
|---|---|---|---|---|
| PR-1 | orkyo-foundation | **merged** 2026-09-15, [#188](https://github.com/Kymr10n/orkyo-foundation/pull/188), squash `02017f9` | 17 of 17 planned packages minus WP-22, plus the review correction, the progress doc, two test fixes and two WP-11 follow-ups | green; shipped as `0.24.2-nightly.20260916.02017f9` |
| PR-4 | orkyo-infra | **open**, [#45](https://github.com/Kymr10n/orkyo-infra/pull/45) | 4 of 5 (WP-22 blocked) | pending; commit 4 needs the migration-gate approval |
| PR-5 | orkyo-documentation | **open**, [#17](https://github.com/Kymr10n/orkyo-documentation/pull/17) | 2 of 2 | pending |
| PR-2 | orkyo-saas | **open**, [#259](https://github.com/Kymr10n/orkyo-saas/pull/259) | 11 of 12 (WP-22 blocked) | first run pending; the branch pins the nightly that carries PR-1 |
| PR-3 | orkyo-community | **open**, [#130](https://github.com/Kymr10n/orkyo-community/pull/130) | 11 of 12 (WP-22 blocked) | first run pending; the branch pins the nightly that carries PR-1 |

All branches are `claude/orkyo-design-review-a6t5ij`. One commit per work package; the subject
names the package.

## 2. PR-1 — orkyo-foundation

| Commit | Package | Note |
|---|---|---|
| docs: withdraw one review finding | — | The review's D1 row 6 was wrong: `frontend/src/App.tsx` exists. Withdrawn in the review file. |
| docs: correct the agent-facing contracts | WP-01 | CLAUDE.md, README, ARCHITECTURE.md, authorization.md, conventions.md, one UI-GUIDELINES.md |
| build: install the pre-commit hooks the guide describes | WP-02 | `setup.sh`, `pre-push` install type, Dependabot `build(deps)` subject |
| test: ratchet five backend conventions; rename ICalendarWriter | WP-06 | 16 / 18 / 11 / 3 / 9 files baselined |
| test: pin ChecksumPolicy and MigrationOrderer; drop the unused revert tree | WP-08 | Decision D-B: the 23 revert files are deleted |
| test: share the authorization conformance walk with the product hosts | WP-05 | `AuthorizationContract` in TestSupport |
| fix(calendar): state the feed token's real scope | WP-09 | Decision D-A resolved to "document": the feed is a shared site calendar by design |
| feat(tenancy): typed tenantless OrgContext and one registration helper | WP-11 | `IOrgContextAccessor`, `AddOrgContextFromHttpContext`, `TenantContextUnavailableException` |
| refactor(frontend): move the feedback outliers onto meta | WP-12 | RequestsPage, AI hooks, announcement editor on `FormDialog`, exemption list |
| feat(worker): one worker loop in core, jobs declared per product | WP-13 | `FoundationWorkerLoop`; the Serilog envelope stays per host (recorded decision) |
| feat(config): AddOrkyoValkey registers the shared multiplexer | WP-14 | |
| feat(migrator): MigrationCli takes its options as a value object | WP-15 | `MigrationCliOptions` |
| feat(migrations): declare tenant-database-only scope on a migration | WP-16 | `MigrationScope`, the two legacy ids marked by name |
| ci: run the audit jobs through this repo's own reusable workflows | WP-18 | Three jobs. `container-scan` and `security-refresh` stay inline: a different scan policy, so the review's "four copies" was three |
| ci: one reusable product CI workflow for saas and community | WP-19 | `reusable-product-ci.yml`, 20 inputs |
| build: shared dev.sh core and synced .claude files | WP-21 | `scripts/dev-common.sh`; `.claude/` rows in `synced-files.manifest` |
| test(frontend): lint rule for hand-rolled empty and loading markup | WP-07 | `orkyo/ui-primitives`; 25 files / 37 sites baselined with file-level disables |
| test: give the exception-mapping test request services | — | The one red test of the first CI run |
| fix(tenancy): tenant settings construct without a tenant | WP-11 | Found in the first local SaaS run: every request without a tenant, `/health` too, returned 500. `TenantSettingsRepository` now takes `IOrgContextAccessor`. CI did not see it because the test host always has a tenant |
| test: hold the keepalive tests on the wire, not a timer | — | `AiChatStreamTests` held a silent turn on a 260 ms timer and expected two 40 ms beats; a loaded runner fitted one. The turn now waits until the body has carried the expected number of keepalives |

Not in PR-1: **WP-22** (per-purpose GitHub tokens). See §6.

Verification: the frontend was linted, type-checked and tested locally (326 files, 4,163 tests).
The backend could not be compiled in the authoring environment (no .NET SDK, no Docker daemon);
PR CI was the first build, and it is green.

## 3. PR-4 — orkyo-infra

| Commit | Package | Note |
|---|---|---|
| chore: scrub the Claude settings allow list | WP-04 | 18 generic entries kept; production host, inline `UPDATE`, developer paths removed; `runbooks/migration-checksum-repair.md` added |
| docs: correct the overview, archive the completed plans, mark the live ones | WP-03 | `docs/plans/` with six files; every plan starts with `Status:` |
| docs: rollback is snapshot-based; there are no revert scripts | WP-08 | `migrations/classification.md` |
| ci: the no-silent-defaults linter covers infra/deploy — **needs approval** | WP-23 | Eight line markers; one is in `check_migration_classification.sh` |

## 4. PR-5 — orkyo-documentation

| Commit | Package |
|---|---|
| docs: Node 24 in the guide | WP-03 |
| docs: say what a calendar feed address exposes | WP-09 |

`npm run build` did not run in the authoring environment (Node 22, no installed dependencies).

## 5. PR-2 and PR-3 — the product branches

Both branches carry every product commit. The CI swap (WP-19 + WP-20) pins
`reusable-product-ci.yml` and every other foundation reusable to the PR-1 merge commit `02017f9`.
The nightly bump of 2026-09-16 (`0.24.2-nightly.20260916.02017f9`) landed on both product mains
with green CI, each branch merged that main, and the PRs opened at 02:45Z.

| Package | orkyo-saas | orkyo-community |
|---|---|---|
| WP-03 docs | done (two commits) | done |
| WP-02 hooks / Dependabot | done (Dependabot only; `setup.sh` was already correct) | done (`setup.sh`, `pre-push`, `.githooks` deleted, Dependabot) |
| WP-10 tenant-header default | done | — |
| WP-05 conformance test | done | done |
| WP-11 OrgContext helper | done | — (Community builds its context from options) |
| WP-13 worker jobs | done | done |
| WP-14 Valkey helper | done | done |
| WP-15 migrator options | — | done |
| WP-16 scope filter | — | done |
| WP-17 admin slot | — | done; the READY-stage path check stays until `TenantApp` gains the slot |
| WP-21 dev.sh / compose | done | done |
| WP-30 dead dependencies | done; `check-dedupe-sync.mjs` flags unused deps (synced file, identical in both) | done |
| WP-19 + WP-20 CI swap | done; the inline jobs became one `product-ci` call | done; the nightly no-change skip rides the audits' `needs` |
| WP-22 tokens | blocked | blocked |

The product backend and shell changes are not compiled or run locally. `bash -n`, `./dev.sh help`,
the YAML loads, `check-dedupe-sync.mjs` and the synced-files check pass.

## 6. Open items for the owner

1. **Update the foundation main ruleset's required checks.** PR-1 is merged with the new names:
   `audit-secrets / audit / secrets + fs`, `audit-nuget / audit / NuGet`, `audit-npm / audit / npm`.
   The product rulesets need the same edit when PR-2 and PR-3 merge: `backend-ci`, `frontend-ci`,
   `smoke-tests` and `Build Images (…)` become `product-ci / …`.
2. **Approve PR-4's fourth commit** (one comment marker in `infra/deploy/check_migration_classification.sh`).
3. **Create the three tokens for WP-22**, then it can be written: `GHCR_PULL_TOKEN` (packages:
   read), `DISPATCH_TOKEN` (actions: write and contents: read on the three product repos),
   `RELEASE_PUSH_TOKEN` (contents: write with the ruleset bypass). `GHCR_TOKEN` stays until one
   release train on staging succeeds with the new secrets.
4. **Review and merge PR-2 and PR-3.** Their CI is the first build of the product backend
   changes against the package that carries PR-1.
5. **Record the Keycloak and MailHog image digests** for the two `compose.local.yml` files; the
   Postgres and Valkey lines carry the digests CI already uses.

## 7. Corrections to the review made while implementing

- D1 row 6 ("foundation has no `App.tsx`") was wrong and is withdrawn.
- The review counted four inline copies of reusable workflows in foundation; `container-scan` and
  `security-refresh` implement a different policy for the Keycloak image and stay inline. Three
  were copies.
- The review's decision default D-A ("filter the feed to the owner") was wrong for the product: the
  subscription dialog offers a site or all sites, and the page in orkyo-documentation already
  describes a site feed. The comment and the page now say so; the code is unchanged.
