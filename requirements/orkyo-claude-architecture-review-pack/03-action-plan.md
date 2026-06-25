# Orkyo Claude Review Action Plan

## Objective

Use Claude Opus on Ultracode to perform a structured architecture and codebase review before any refactoring starts.

The output should become a practical refactoring backlog, not a theoretical architecture essay.

## Phase 0 — Prepare the Review

### Tasks

1. Ensure the repository builds from a clean checkout.
2. Ensure tests and linters are executable.
3. Ensure demo/seed logic is in a known state.
4. Create a temporary branch for the audit, for example:

```bash
git checkout -b audit/architecture-dry-kiss-review
```

5. Provide Claude with:

- this review pack
- repository root
- current architecture notes if available
- known concerns about complexity
- current product priorities

### Expected Output

No code changes. Claude should only produce the audit report.

## Phase 1 — Run Audit Only

### Tasks

Ask Claude to execute the prompt from `02-claude-ultracode-prompt.md`.

Claude must inspect the codebase and produce:

- architecture/codebase audit
- complexity map
- prioritized findings
- first safe PR proposal
- backlog-ready tickets

### Review Gate

Before implementation, manually review:

- whether findings are concrete and file-based
- whether recommendations preserve tenant isolation
- whether proposed simplifications actually reduce code or concepts
- whether any recommendation introduces unnecessary abstraction
- whether the first PR is small enough

Reject vague findings.

## Phase 2 — Triage Findings

Classify each finding into one of these buckets:

### Fix Now

Use for:

- tenant isolation risks
- duplicated security checks
- obvious dead code
- obvious dependency cleanup
- low-risk UX standardization

### Schedule

Use for:

- medium-sized consolidation
- API boundary cleanup
- shared UI pattern standardization
- service/domain simplification

### Defer

Use for:

- large refactors
- speculative optimizations
- changes that require product decisions
- changes with unclear ROI

### Do Not Touch

Use for:

- stable, sensitive areas
- complex but justified code
- areas where refactor risk exceeds benefit

## Phase 3 — Create the First Safe PR

The first PR should be intentionally small.

Recommended first PR types:

- remove dead code
- remove unused dependencies
- standardize one repeated empty/loading/error state
- consolidate one duplicated helper
- rename one inconsistent concept where impact is localized
- add missing tenant-isolation regression tests without changing behavior

Avoid as first PR:

- domain model rewrites
- large component refactors
- route restructuring
- database schema changes
- broad UI redesign
- new libraries

### PR Acceptance Criteria

- behavior preserved
- tests pass
- build passes
- no tenant isolation regression
- code volume reduced or consistency improved
- rollback is simple

## Phase 4 — Execute Iterative Refactoring

Work in small PRs.

Recommended PR sequence:

1. deletion-only cleanup
2. dependency cleanup
3. shared UI state standardization
4. table/card/dialog pattern standardization
5. frontend data-fetching consolidation
6. API response/error standardization
7. validation boundary cleanup
8. tenant scoping hardening
9. domain terminology cleanup
10. larger architectural simplifications only after previous phases are stable

Each PR should answer:

- What complexity was removed?
- What duplicate pattern was eliminated?
- What user-facing behavior changed, if any?
- How was tenant isolation validated?
- How can this be rolled back?

## Phase 5 — Establish Guardrails

After cleanup, add lightweight guardrails so complexity does not grow again.

Recommended guardrails:

- architecture decision records for major patterns
- frontend component usage guidelines
- backend route/service/repository conventions
- tenant isolation checklist for new endpoints
- UI consistency checklist for new screens
- dependency approval rule
- dead-code cleanup expectation during feature work
- PR template section for DRY/KISS impact

## Suggested Review Cadence

For a solo/founder-led product, keep the review cadence pragmatic:

- large audit: before beta expansion or major launch milestones
- small architecture review: every major feature epic
- dependency cleanup: monthly or before release
- tenant isolation/security review: before exposing new reporting/export/public demo capabilities

## Definition of Success

The review is successful if it produces:

- fewer competing patterns
- clearer domain terminology
- less duplicated frontend and backend logic
- safer tenant isolation
- more consistent UX
- smaller future change surface
- a realistic refactoring backlog
- no unnecessary rewrite pressure
