# Orkyo — Criterion Applicability Scope MD Pack

This pack contains implementation instructions for introducing criterion applicability scope tags so criteria can be targeted to Spaces, People, and Tools, then filtered consistently across Settings and resource-domain pages.

Recommended execution order:

1. `01-domain-specification.md`
2. `02-backend-implementation.md`
3. `03-frontend-implementation.md`
4. `04-migration-and-compatibility.md`
5. `05-acceptance-criteria.md`

Core decision: keep Criteria as the global canonical registry, but add an applicability scope to every criterion. Resource domains consume filtered subsets only.
