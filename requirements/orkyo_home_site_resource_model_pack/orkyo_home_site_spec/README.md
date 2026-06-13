# Orkyo Home Site / Current Site Resource Model Implementation Pack

This pack defines the holistic design and implementation plan for introducing a unified resource location model across spaces, people, tools, and requests.

## Files

1. `01-specification.md` — functional and architectural specification
2. `02-database-changes.md` — proposed schema changes, migration strategy, constraints, and compatibility notes
3. `03-ui-process-changes.md` — UI, UX, validation, scheduling, and operational process changes
4. `04-implementation-plan-for-copilot.md` — phased implementation plan with concrete tasks and acceptance criteria

## Core decision

All resources have a `home_site_id`.

Spaces are the immovable special case:

- `home_site_id` is required
- `current_site_id = home_site_id`
- `cross_site_allowed = false`

People and tools may move or be assigned cross-site depending on configuration and scheduling rules.

Requests may optionally be site-scoped. A request without a site can be scheduled at any site; a site-scoped request must be fulfilled at that site unless an explicit override policy is introduced later.
