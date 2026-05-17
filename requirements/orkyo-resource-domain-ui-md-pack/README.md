# Orkyo Resource Domain UI Refactoring Pack

This pack contains implementation instructions for Copilot to refactor Orkyo's domain administration UX.

## Objective

Move resource-type-specific master data out of global Settings and into the corresponding resource domain pages:

- People owns departments, job titles, people groups, absences, and people-specific criteria/skills.
- Spaces owns space groups and space-specific criteria/capabilities.
- Settings remains responsible only for tenant-wide configuration and global reusable definitions.

## Documents

1. `01-target-information-architecture.md`  
   Defines the target UX and ownership model.

2. `02-domain-model-and-data-boundaries.md`  
   Defines domain ownership, backend boundaries, and data semantics.

3. `03-frontend-implementation-plan.md`  
   Concrete frontend refactoring plan for navigation, tabs, and views.

4. `04-backend-api-and-permission-plan.md`  
   API, authorization, and service-layer expectations.

5. `05-migration-and-compatibility-plan.md`  
   Migration strategy, routing compatibility, and cleanup.

6. `06-acceptance-criteria.md`  
   Testable implementation acceptance criteria.

7. `copilot-execution-prompt.md`  
   Single prompt to give Copilot before implementation.
