# Orkyo Home Site / Current Site Resource Model Implementation Pack

> **Superseded note (2026-06):** The **stored** `current_site_id` column described throughout this
> pack has been removed (migration `1560.foundation.drop_current_site_id.sql`). It was a manually
> maintained value that drifted from reality. `current_site_id` now lives on as a **derived,
> read-only** field on the resource read model (`ResourceInfo.currentSiteId`): for a space it is
> `spaces.site_id`; for a person/tool it is the site of the non-cancelled assignment overlapping the
> current time, else `home_site_id`. It is computed, never written. Cross-site validation compares the
> request's site against `home_site_id` (+ `cross_site_allowed`); cross-site time clashes surface via
> the existing overbooking warning. The surviving **stored** fields are `home_site_id` and
> `cross_site_allowed`; read the `current_site_id` write/column references below as historical context.

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
