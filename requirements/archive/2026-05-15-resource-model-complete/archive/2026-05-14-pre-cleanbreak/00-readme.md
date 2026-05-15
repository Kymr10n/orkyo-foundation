# Orkyo Resource Model Refactoring Pack

> **Read first:** `07-current-state-analysis.md` (findings against the actual
> foundation code) and `08-revised-implementation-path.md` (revised phase plan).
> Docs 01–06 describe the target model; docs 07–08 reconcile it with what
> already exists and supersede doc 03 where they conflict. Decisions in
> doc 08 §"Open questions" must be confirmed before Phase 1 begins.

## Purpose

This pack describes the planned transition from a Space-first scheduling model to a Resource-first scheduling model.

The architectural objective is:

> Orkyo schedules requests against resources, capabilities, availability, and allocation constraints.  
> A Space becomes one resource type among others, while still remaining a first-class UX/domain concept.

## Initial Scope

Implement the new resource model with three system resource types:

- Space
- Person
- Tool

Do not implement user-defined custom resource types in the first iteration. The model must be designed so that custom resource types can be introduced later without reworking the core architecture.

## Key Principle

Do not break existing Space planning behavior during the migration.

The transition must be incremental:

1. Introduce the generic resource model in parallel.
2. Create Space resources from existing Spaces.
3. Move scheduler internals from Space assignments to Resource assignments.
4. Activate Person and Tool resources only after Space migration is stable.

## Required Documents

- `01-resource-domain-model.md`
- `02-database-and-api-model.md`
- `03-migration-path.md`
- `04-scheduler-integration.md`
- `05-copilot-implementation-rules.md`
- `06-acceptance-criteria.md`
