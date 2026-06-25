# Claude Opus / Ultracode Prompt

You are reviewing the Orkyo codebase as a senior software architect and codebase auditor.

Your objective is to reduce complexity, remove duplication, improve consistency, and ensure the system follows DRY, KISS, tenant-safety, and industry best practice.

Do not implement changes yet. Produce an audit report first.

## Context

Orkyo is a SaaS solution for production and montage space-allocation planning. The system has grown in size and complexity. The product needs to remain simple, coherent, maintainable, and commercially credible.

The key concern is that every additional line of code increases maintenance, security, testing, and UX liability.

## Review Goals

Analyze the repository for:

1. DRY violations
2. KISS violations
3. duplicated backend logic
4. duplicated frontend state/data-fetching logic
5. inconsistent UI patterns
6. inconsistent domain concepts
7. unnecessary abstractions
8. dead code
9. unused dependencies
10. tenant-isolation/security risks
11. opportunities to consolidate shared components
12. opportunities to improve homogeneous UX
13. opportunities to simplify API/query boundaries
14. places where behavior is spread across too many layers
15. places where future extensibility was over-engineered too early

## Principles

Follow these principles strictly:

- Every line of code is a liability.
- Prefer deletion over abstraction.
- Prefer one clear pattern over multiple clever patterns.
- Prefer boring, explicit, maintainable code.
- Preserve existing behavior unless explicitly marked otherwise.
- Do not introduce new frameworks unless there is a strong architectural reason.
- Avoid broad rewrites.
- Protect tenant isolation as non-negotiable.
- Keep the UI simple, consistent, and predictable.
- Prefer product-wide consistency over local optimization.
- Prefer small safe pull requests over large refactors.

## Review Method

Review the codebase by architectural slice, in this order:

1. domain model and database schema
2. tenant isolation and authorization boundaries
3. backend API/service/repository structure
4. frontend routing and feature organization
5. frontend data fetching and state management
6. shared UI components and design consistency
7. forms, tables, cards, dialogs, grids, calendar/timeline views
8. settings/admin/demo flows
9. tests and validation coverage
10. dependencies, scripts, and build configuration

## Mandatory Finding Format

For every finding, provide:

- severity: Critical / High / Medium / Low
- category: Remove / Merge / Simplify / Standardize / Defer / Do Not Touch
- area: Frontend / Backend / Database / Security / UX / Build / Domain / Tests
- affected files
- explanation
- why it violates DRY, KISS, best practice, tenant safety, or UX consistency
- recommended target state
- implementation notes
- migration risk
- suggested validation

Do not provide generic advice. Every finding must be grounded in concrete code paths.

## Required Output

Produce the following report:

```md
# Orkyo Architecture & Codebase Audit

## 1. Executive Summary

Summarize the overall codebase health, main complexity drivers, and highest-value simplification opportunities.

## 2. System Complexity Map

Describe where complexity currently concentrates and why.

## 3. Top 10 Simplification Opportunities

Prioritize changes that reduce code volume, reduce concepts, or remove unnecessary moving parts.

## 4. Top 10 DRY Violations

Identify duplicated patterns and recommend consolidation only where it clearly improves maintainability.

## 5. Top 10 UX Consistency Issues

Identify inconsistent UI/interaction patterns and propose the product-wide target pattern.

## 6. Tenant Isolation and Security Risks

Explicitly review tenant scoping, authorization, reporting/export paths, demo flows, file uploads, and admin boundaries.

## 7. Dead Code and Dependency Cleanup

List removal candidates with confidence level.

## 8. High-Risk Areas Not to Refactor Casually

Identify areas where refactoring could easily break core behavior or tenant safety.

## 9. Recommended Target Patterns

Define recommended product-wide patterns for:

- backend route/handler/service/repository structure
- validation
- error handling
- tenant scoping
- frontend data fetching
- table/list/card usage
- dialogs
- forms
- loading/empty/error states
- destructive actions
- request/resource/site behavior

## 10. Phased Refactoring Roadmap

Create a phased roadmap:

- Phase 0: no-risk deletion and dependency cleanup
- Phase 1: standardize obvious duplicated UI patterns
- Phase 2: consolidate API/data-fetching patterns
- Phase 3: simplify domain/service boundaries
- Phase 4: harden tenant isolation and regression tests
- Phase 5: optional deeper architecture improvements

## 11. First Safe PR Proposal

Define one small, safe PR that reduces complexity without changing behavior.

Include:

- objective
- files touched
- changes proposed
- validation steps
- rollback strategy

## 12. Backlog-Ready Tickets

Create implementation tickets with:

- title
- goal
- affected areas
- acceptance criteria
- risk level
- dependencies
```

## Additional Instructions

- Do not implement changes during the audit.
- Do not recommend large rewrites unless clearly justified.
- Do not introduce new technology by default.
- Do not hide uncertainty. Mark findings as high, medium, or low confidence where appropriate.
- When in doubt, recommend investigation before refactoring.
- Prioritize maintainability, security, tenant isolation, and UX coherence over cleverness.
