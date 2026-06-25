# Orkyo Architecture & Codebase Review Specification

## Objective

Review the Orkyo codebase with the objective of reducing complexity, improving maintainability, enforcing DRY and KISS principles, and ensuring a homogeneous user experience across the product.

This is an audit-first engagement. Do not implement changes during the initial review.

The guiding principle is:

> Every line of code is a liability. Prefer deletion, consolidation, and simplification over abstraction or expansion.

## Scope

The review covers the full Orkyo SaaS repository, including but not limited to:

- frontend application structure
- routing and navigation
- state management and data-fetching patterns
- shared UI components
- forms, dialogs, tables, cards, grids, calendar views, settings pages
- backend API structure
- backend service/domain boundaries
- request/resource/site/tenant domain model
- database schema and migrations
- authorization and tenant isolation
- error handling, validation, logging, and observability
- test structure and test coverage strategy
- dependencies and build configuration
- demo, marketing, and tenant bootstrap flows where part of the repository

## Non-Negotiable Constraints

- Tenant isolation must not regress.
- Existing functional behavior must be preserved unless explicitly flagged for removal.
- No broad rewrite without explicit architectural justification.
- No new framework unless it eliminates materially more complexity than it introduces.
- Prefer standardization over feature expansion.
- Prefer explicit simple code over clever generic abstractions.
- Prefer one product-wide UX pattern over multiple local variants.
- Prefer removing dead code over refactoring it.

## Review Dimensions

### 1. DRY Compliance

Identify duplication across:

- frontend components
- API clients and hooks
- backend handlers/services/repositories
- validation logic
- authorization checks
- tenant scoping
- error/loading/empty state handling
- table/grid/card implementations
- form layouts and dialog patterns
- request/resource/site logic
- reporting/insight/query structures

Classify each duplication as one of:

- acceptable local duplication
- should be merged
- should be standardized
- should be deleted
- should remain separate for clarity

### 2. KISS Compliance

Identify areas where the codebase is more complex than required:

- unnecessary abstraction layers
- premature generalization
- over-configurable components
- indirection without clear benefit
- deeply nested conditionals
- fragmented domain logic
- unclear naming
- multiple ways to perform the same operation
- redundant state derivation
- unnecessary data transformations

### 3. UX Homogeneity

Review whether the product behaves consistently across major flows:

- navigation
- master data management
- resources: spaces, people, tools
- requests
- utilization views
- calendar/timeline/grid views
- settings/admin pages
- conflict visualization
- empty states
- loading states
- error handling
- confirmations
- destructive actions
- filters and search
- pagination or virtualized lists

Identify where users experience inconsistent patterns for similar tasks.

### 4. Domain Consistency

Review whether the domain model is internally coherent:

- tenant
- site
- resource
- space
- person
- tool
- request
- schedule
- assignment
- conflict
- criteria
- utilization
- quota
- demo tenant

Check for duplicated or contradictory interpretations of these concepts across frontend, backend, database, and seed/demo logic.

### 5. Backend Architecture

Review:

- API route design
- handler/service/repository boundaries
- validation boundaries
- error response structure
- pagination/filtering conventions
- tenant scoping enforcement
- N+1 query risks
- overly broad fetches
- query duplication
- transaction handling
- migration quality
- seed/demo reset logic

### 6. Frontend Architecture

Review:

- component hierarchy
- route structure
- shared UI primitives
- feature modules
- API hooks
- local/global state usage
- form handling
- dialog handling
- optimistic updates
- cache invalidation
- accessibility basics
- responsive/tablet readiness
- visual consistency

### 7. Security and Tenant Isolation

Review:

- tenant filtering in every query path
- backend authorization checks
- frontend assumptions that must not be trusted
- admin/site-admin/user boundary clarity
- public demo behavior
- file upload handling
- reporting/export endpoints
- cross-tenant leakage risks
- logging of sensitive data

Tenant isolation findings must be classified as Critical or High unless demonstrably harmless.

### 8. Dependencies and Build Surface

Review:

- unused dependencies
- duplicate libraries solving the same problem
- heavy dependencies for small functionality
- custom code that should use existing project primitives
- scripts and CI/CD complexity
- environment configuration duplication

## Finding Format

Every finding must use this structure:

```md
## Finding: <short title>

Severity: Critical | High | Medium | Low
Category: Remove | Merge | Simplify | Standardize | Defer | Do Not Touch
Area: Frontend | Backend | Database | Security | UX | Build | Domain | Tests
Affected files:
- path/to/file
- path/to/other-file

Current state:
<what exists today>

Why this matters:
<why this violates DRY/KISS/best practice or creates UX inconsistency>

Recommended target state:
<what should exist instead>

Implementation notes:
<practical guidance>

Migration risk:
Low | Medium | High

Suggested validation:
<tests, manual checks, tenant isolation checks, regression checks>
```

## Required Outputs

Claude must produce the following documents or sections:

1. Executive summary
2. System complexity map
3. Top 10 simplification opportunities
4. Top 10 DRY violations
5. Top 10 UX consistency issues
6. Top 10 tenant/security risks
7. Dead code and dependency cleanup candidates
8. High-risk areas not to refactor casually
9. Recommended target patterns
10. Phased refactoring roadmap
11. First safe PR proposal
12. Backlog-ready ticket list

## Acceptance Criteria

The review is acceptable only if:

- every major finding references concrete files
- recommendations are actionable
- tenant isolation is explicitly reviewed
- UX consistency is reviewed across multiple flows
- findings are prioritized by impact and risk
- the first PR proposal is small and safe
- no broad rewrite is recommended without strong evidence
- deletion and consolidation opportunities are explicitly identified
