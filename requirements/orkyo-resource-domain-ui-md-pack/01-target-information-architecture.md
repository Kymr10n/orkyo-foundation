# 01 — Target Information Architecture

## Goal

Refactor the Orkyo administration UX so resource-domain-specific master data is managed inside the owning resource domain instead of the global Settings page.

The current Settings page is becoming overcrowded because it mixes:

- tenant-wide configuration,
- reusable global definitions,
- people-specific master data,
- space-specific master data,
- future resource-type-specific master data.

This does not scale as Orkyo evolves from a space-planning application into a generic resource allocation platform.

## Design Principle

A configuration object should live where it is primarily consumed and operationally maintained.

Use this rule:

| Object | Owning UI Area | Reason |
|---|---|---|
| Organization settings | Settings | Tenant-wide configuration |
| Scheduling settings | Settings | Tenant-wide scheduling behavior |
| System configuration | Settings | Platform behavior |
| Criteria registry | Settings | Global reusable ontology |
| Departments | People | People-specific master data |
| Job titles | People | People-specific master data |
| People groups | People | People resource grouping |
| Absences | People | People availability |
| People skills/criteria | People | People-specific capability use |
| Space groups | Spaces | Space resource grouping |
| Space capabilities/criteria | Spaces | Space-specific capability use |
| Floorplans | Spaces | Space-specific visualization |

## Global Navigation Target

Keep the current left navigation for now:

- Utilization
- Spaces
- People
- Requests
- Conflicts
- Settings

Do not introduce a full `Resources` parent navigation item yet unless the existing routing shell already supports it cleanly. This can be deferred.

## Settings Page Target

The Settings page should only expose global administration concerns.

Recommended tabs after this refactoring:

- Criteria
- Templates
- Presets
- Users
- Organization
- Scheduling
- Configuration

Remove these tabs from Settings:

- Departments
- Job Titles
- Groups, if the current Groups tab is resource-type-specific

If the current Groups implementation is already generic and cross-resource, keep only a global registry view if required. Otherwise, move group administration into resource domains.

## People Page Target

The People page becomes the bounded context for people resources.

Recommended tabs:

- People
- Groups
- Absences
- Departments
- Job Titles
- Skills / Criteria

Naming recommendation:

- Use `Skills` in the People UI if the underlying object is Criteria filtered to `resourceType = people`.
- Avoid exposing the technical term `Criteria` to end users where `Skills` is more intuitive.

## Spaces Page Target

The Spaces page becomes the bounded context for space resources.

Recommended tabs:

- Spaces
- Groups
- Capabilities / Criteria
- Availability / Maintenance, if already supported or planned
- Floorplan

Naming recommendation:

- Use `Capabilities` in the Spaces UI if the underlying object is Criteria filtered to `resourceType = space`.
- The global definition can still be called Criteria in Settings.

## Empty State Requirements

Each new tab must have a clear empty state:

- Space Groups: `No space groups defined yet.`
- Space Capabilities: `No space capabilities defined yet.`
- Departments: `No departments defined yet.`
- Job Titles: `No job titles defined yet.`
- People Skills: `No people skills defined yet.`

Empty states must include an action button if the user has permission to create entries.

## UX Consistency

Use the existing Orkyo visual language:

- same tab component style as current People and Settings pages,
- same action-button positioning,
- same table/card/list treatment,
- same empty-state pattern,
- same dialog pattern for create/edit/delete.

Do not introduce a new UI component library or visual pattern.
