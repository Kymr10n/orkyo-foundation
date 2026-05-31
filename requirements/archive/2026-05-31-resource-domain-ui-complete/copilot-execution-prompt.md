# Copilot Execution Prompt

You are implementing a UI and domain-boundary refactoring in Orkyo.

## Objective

Move resource-domain-specific administration out of global Settings and into the owning resource pages.

Implement the target model described in this pack:

- People owns Departments, Job Titles, People Groups, Absences, and People Skills/Criteria.
- Spaces owns Space Groups and Space Capabilities/Criteria.
- Settings keeps only global reusable definitions and tenant-wide configuration.

## Mandatory Rules

1. Investigate the existing implementation before changing code.
2. Do not duplicate data models.
3. Do not fork Criteria into PeopleCriteria and SpaceCriteria unless the existing architecture already requires it.
4. Do not fork Groups into separate physical models unless the existing architecture already requires it.
5. Prefer resource-type filtering over duplicate implementations.
6. Preserve existing floorplan and spaces behavior.
7. Preserve existing people and absence behavior.
8. Preserve tenant isolation.
9. Enforce resource-type filtering server-side.
10. Add backward-compatible redirects for old Settings routes.
11. Remove dead code after migration.
12. Follow existing project conventions for components, hooks, API clients, routes, tests, and migrations.

## Implementation Order

1. Inventory current Settings, People, Spaces, Criteria, Groups, Departments, and Job Titles code.
2. Identify existing data model support for resource type filtering.
3. Add minimal backend/data changes only if required.
4. Add People tabs: Groups, Departments, Job Titles, Skills/Criteria.
5. Add Spaces tabs: Groups, Capabilities/Criteria.
6. Move/reuse existing Settings UI components into domain pages.
7. Clean up Settings tabs.
8. Add redirects from old Settings routes.
9. Add/update tests.
10. Remove dead code.

## Expected Result

After implementation:

- Settings is less crowded and only contains global administration.
- People becomes the correct home for people-specific master data.
- Spaces becomes the correct home for space-specific master data.
- Criteria remain globally defined but are filtered and managed contextually by resource domain.
- Groups are resource-type-specific and shown only in their owning domain.
