# Availability Event Model

## Objective

Replace the current `off_times` concept with a lean but extensible availability model that supports:

- Site-wide holidays and shutdowns
- Partial closures
- Resource-specific absences
- Resource-type-specific availability
- Future resource types without schema redesign

The model must work consistently for:

- Spaces
- People
- Machinery
- Tools
- Future resource types

---

# Core Concepts

## 1. Availability Events

Availability events are site-scoped operational events that affect one or more resources.

Examples:

- Public holiday
- Site shutdown
- Maintenance window
- Reduced-capacity operation
- Emergency closure

Availability events define a default state and optional scoped overrides.

---

## 2. Resource Absences

Resource absences are individual resource unavailability entries.

Examples:

- Vacation
- Sickness
- Training
- Personal unavailability
- Machine repair

These are always tied directly to a single resource.

---

# Database Design

## availability_events

```sql
id
site_id
title
description
event_type
start_ts
end_ts
default_effect
created_at
updated_at
```

### event_type

Examples:

- public_holiday
- shutdown
- maintenance
- reduced_capacity
- custom

### default_effect

Examples:

- closed
- available
- reduced_capacity

This defines the default behavior for all resources at the site.

---

## availability_event_scopes

```sql
id
availability_event_id
target_type
target_id
effect
capacity_percent nullable
```

### target_type

Examples:

- resource
- resource_group
- resource_type

### effect

Examples:

- available
- closed
- reduced_capacity

---

## resource_absences

```sql
id
resource_id
absence_type
start_ts
end_ts
capacity_percent nullable
notes
created_at
updated_at
```

### absence_type

Examples:

- vacation
- sickness
- unavailable
- maintenance
- custom

---

# Scheduling Logic

## Resource Availability Calculation

A resource is considered unavailable when:

```text
base availability
- matching availability events
- matching resource absences
- existing allocations
```

---

## Availability Event Resolution

Algorithm:

1. Load all active availability events for the site
2. Apply `default_effect`
3. Apply matching scoped overrides
4. Resolve final effective availability

---

# Example Scenarios

## Public Holiday

```text
Site: Zürich Plant
Default Effect: closed
```

Overrides:

```text
Machinery Group "Production Line A" => available
Person "John Doe" => available
Space "Control Room" => available
```

Result:

- Most resources unavailable
- Selected production resources remain operational

---

## Maintenance Window

```text
Site: Zürich Plant
Default Effect: available
```

Overrides:

```text
Machine Group "Cooling Systems" => closed
```

Result:

- Site operational
- Specific systems unavailable

---

# UI Requirements

## Availability Event UI

Create a dedicated management UI for site-level events.

Features:

- Site-scoped calendar/grid
- Default effect selection
- Optional scoped overrides
- Resource picker
- Resource group picker
- Resource type picker

---

## Resource Absence UI

Manage directly on resource pages.

Examples:

- People → vacation/sickness
- Machines → maintenance
- Tools → unavailable

---

# Migration

## Remove

Remove old:

- `off_times`
- `off_time_resources`
- `applies_to_all_resources`

---

## Introduce

Add:

- `availability_events`
- `availability_event_scopes`
- `resource_absences`

---

# Design Principles

## Keep Lean

Avoid:

- Deep rule engines
- Complex inheritance systems
- Nested overrides
- Recursive availability logic

---

## Keep Flexible

Support:

- Future resource types
- Partial closures
- Reduced-capacity operations
- Per-resource overrides

Without schema redesign.

---

# Important

Availability events are NOT the same as absences.

| Concept | Scope |
|---|---|
| availability_event | Site / operational |
| resource_absence | Individual resource |

Keep both concepts separate.
