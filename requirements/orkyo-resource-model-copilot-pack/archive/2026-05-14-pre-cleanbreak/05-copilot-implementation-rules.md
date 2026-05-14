# Copilot Implementation Rules for Resource Model Refactoring

## Prime Directive

Implement the resource model incrementally and safely.

Do not rewrite the scheduler, Space UI, or Request model in one large change unless explicitly instructed.

Each step must be buildable, testable, and reversible.

## Architecture Rules

- Put reusable domain abstractions in `orkyo-foundation`.
- Put SaaS-specific orchestration in `orkyo-saas`.
- Do not place multi-tenant SaaS assumptions inside generic foundation domain code.
- Do not duplicate resource logic between SaaS and Community.
- Do not hardcode resource type strings in business logic.
- Use constants/enums for stable keys.
- Do not introduce custom ResourceType UI in the first iteration.
- Keep Space as a first-class UX concept.

## Code Quality Rules

Follow:

- DRY
- KISS
- SOLID where useful
- clear service boundaries
- explicit validation
- secure defaults
- no silent fallback behavior
- no speculative abstractions beyond the agreed model

Avoid:

- string literals for domain keys
- duplicated validation logic
- hidden synchronization logic
- broad unrelated refactoring
- changing architecture without documenting the reason
- workaround fixes without root cause analysis

## Migration Safety Rules

- Migrations must be idempotent where possible.
- Existing data must not be destroyed.
- Existing Space scheduling behavior must remain operational until explicitly migrated.
- Prefer dual-write before read-switch.
- Do not remove `Request.SpaceId` or equivalent legacy fields early.
- Use feature flags for critical behavioral switches where practical.
- Add consistency checks during migration.

## Testing Rules

For every migration phase:

1. Add or update automated tests.
2. Verify existing Space behavior still passes.
3. Add resource-specific tests for new behavior.
4. Include negative tests for invalid assignments.
5. Include migration tests for existing Space data.

## Documentation Rules

Update or create documentation for:

- resource model
- migration phase
- database schema
- scheduler behavior
- known limitations
- follow-up tasks

## Pull Request Structure

Prefer small PRs in this sequence:

1. Resource domain contracts and constants
2. Database migrations and seed data
3. Resource services and validation
4. Space-to-resource mapping
5. Capability migration
6. ResourceAssignment dual-write
7. Scheduler read switch
8. Person and Tool activation
9. Utilization reporting

## Required Copilot Behavior

Before modifying existing scheduler logic:

1. Identify current behavior.
2. Identify affected files.
3. Explain intended change.
4. Preserve existing behavior unless explicitly changed.
5. Add tests before or together with the change.

## Explicit Non-Goals

Do not implement:

- full ERP resource management
- payroll
- procurement
- asset lifecycle management
- custom resource type management UI
- advanced optimization solver
- complex inheritance trees
- arbitrary metadata-driven UI builder
