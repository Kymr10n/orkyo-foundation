# 01 — Functional Specification

## Objective

Refactor the People skills UX so that criteria definitions remain globally managed in `Settings → Criteria`, while People only manages skill assignments per person.

The People page must not duplicate the criteria registry. It should provide a clear operational workflow for assigning person-applicable criteria to a selected person.

## Current Problem

The current `People → Skills` tab lists people and provides a `Manage skills` action. This tab is conceptually weak because it creates a separate skills management area under People, even though skills are actually criteria filtered by person applicability.

This causes these issues:

- unclear ownership of criteria definitions
- duplicated navigation patterns
- future inconsistency across resource types
- difficulty scaling the UX to tools, spaces, and future resource types

## Target Model

### Criteria Definition

A criterion is a reusable definition stored in the global criteria registry.

A criterion can be applicable to one or more resource types:

- `person`
- `space`
- `tool`
- future custom resource types

Examples:

| Criterion | Type | Applicable To |
|---|---|---|
| Project Management | Boolean | Person |
| Forklift License | Boolean | Person |
| Capacity (persons) | Number | Space |
| AV Equipment | Boolean | Space |
| Max Load | Number | Tool |

### Skill

A skill is not a separate definition. A skill is a person assignment of a criterion that is applicable to people.

Example:

```text
Person: Hans Huber
Criterion: Project Management
Value: true
```

## Navigation and UX

### Settings → Criteria

This remains the authoritative criteria registry.

The page must support:

- listing all criteria
- filtering by applicability: All, Spaces, People, Tools
- creating new criteria
- editing criteria
- deleting criteria where allowed
- managing applicability tags

When a criterion is created from this page, the user explicitly selects applicability.

### People → People

The People list becomes the primary place for assigning skills to people.

Each person row should include a dedicated action:

```text
Manage Skills
```

Recommended actions per person:

- Edit person
- Manage skills
- Manage absences
- Delete person

The skill count may be shown as a column or compact badge.

Example table:

| Name | Email | Job Title | Department | Skills | Actions |
|---|---|---|---|---|---|
| Hans Huber | hans@huber.com | - | - | 1 | Edit / Skills / Absences / Delete |

### People → Skills Tab

Remove the standalone `Skills` tab from People navigation.

Reason:

Skills are assignments, not a separately managed entity. The correct entry point is the individual person row.

If the tab is kept temporarily for migration safety, it must only redirect or visually guide the user back to the People list. It must not remain as a parallel management surface.

## Manage Skills Dialog / Drawer

Clicking `Manage Skills` opens a modal or side drawer for the selected person.

Title:

```text
Manage Skills — Hans Huber
```

The dialog must list all criteria where applicability includes `person`.

It must allow assigning or updating a value per criterion.

### Type-Specific Editors

The input control depends on criterion type:

| Criterion Type | Control |
|---|---|
| Boolean | Checkbox / toggle |
| Number | Number input |
| Text | Text input |
| Enum | Select |
| Multi-select | Multi-select / tag selector |
| Date | Date picker, if supported |

### Empty State

If no person-applicable criteria exist, show:

```text
No person-applicable criteria exist yet. Create a skill criterion to start assigning skills to people.
```

Provide an action:

```text
Add Skill Criterion
```

## Quick Create from Manage Skills Dialog

The Manage Skills dialog should allow creating a missing skill criterion without navigating to Settings.

Button:

```text
Add Skill Criterion
```

Behavior:

- opens criterion creation dialog
- defaults applicability to `person`
- locks or preselects `person` applicability by default
- still creates a normal global criterion in the criteria registry
- after creation, the new criterion appears immediately in the Manage Skills dialog

Important: this must not create a separate people-only skill entity.

## Data Ownership Rules

### Criteria

Owned by global settings.

Fields should include at minimum:

- id
- name
- data type
- applicable resource types
- allowed values, where relevant
- created timestamp
- updated timestamp

### Person Skill Assignment

Owned by People/resource assignment domain.

Fields should include at minimum:

- person id
- criterion id
- value
- created timestamp
- updated timestamp

## Business Rules

1. A person can only be assigned criteria where `applicableResourceTypes` contains `person`.
2. The backend must enforce this rule. The frontend filter is not sufficient.
3. If a criterion loses `person` applicability while assigned to people, the system must prevent the change or require explicit cleanup.
4. Deleting a criterion with existing assignments must be blocked unless cascading deletion is explicitly implemented and confirmed.
5. Quick-created criteria from the People context must default to person applicability.
6. Assignment values must be validated according to the criterion data type.

## API Expectations

Use existing API patterns where possible. Avoid introducing People-specific criteria endpoints if generic resource assignment endpoints already exist.

Recommended conceptual endpoints:

```http
GET /api/criteria?applicableTo=person
POST /api/criteria
GET /api/people/{personId}/criteria-values
PUT /api/people/{personId}/criteria-values/{criterionId}
DELETE /api/people/{personId}/criteria-values/{criterionId}
```

If the resource model already provides generic assignment APIs, prefer:

```http
GET /api/resources/{resourceId}/criteria-values
PUT /api/resources/{resourceId}/criteria-values/{criterionId}
```

The implementation must follow the current Orkyo architecture and naming conventions.

## Non-Goals

This change does not implement:

- full resource-type customization
- matching algorithm changes
- auto-scheduling skill scoring
- group-inherited skills
- certification expiry handling
- skill proficiency matrices

Those can be added later on top of this model.
