# 01 — Domain Specification: Criterion Applicability Scope

## Objective

Introduce applicability scope metadata on Criteria so each criterion can be targeted to one or more resource domains:

- Spaces
- People
- Tools

This allows Criteria to remain a global reusable ontology while preventing irrelevant criteria from appearing in resource-specific workflows.

## Current Problem

The Settings page currently lists all criteria without differentiating whether a criterion is intended for:

- space capabilities,
- people skills/qualifications,
- tool capabilities,
- request requirements,
- or future resource types.

As Orkyo introduces additional resource types, the criteria registry will become noisy and difficult to use unless criteria can be filtered by applicability.

## Domain Principle

Criteria are global definitions.

Resource pages do not own the canonical criterion definition. They consume only the subset of criteria applicable to their resource type.

Therefore:

- Settings owns creation and editing of Criteria.
- Criteria include applicability scope.
- Spaces consume criteria where `appliesTo` includes `spaces`.
- People consume criteria where `appliesTo` includes `people`.
- Tools consume criteria where `appliesTo` includes `tools`.

## Conceptual Model

```text
Criterion
  id
  name
  description
  dataType
  appliesTo[]
    - spaces
    - people
    - tools
  createdAt
  updatedAt
```

A criterion may apply to multiple resource types.

Examples:

| Criterion | Data Type | Applies to Spaces | Applies to People | Applies to Tools |
|---|---|---:|---:|---:|
| `av_equipment` | Boolean | yes | no | yes |
| `capacity_persons` | Number | yes | no | no |
| `forklift_certified` | Boolean | no | yes | no |
| `power_400v` | Boolean | yes | no | yes |
| `projector_lumens` | Number | yes | no | yes |

## Naming

Use an explicit enum-style model in code. Avoid free-form strings where possible.

Recommended enum values:

```text
spaces
people
tools
```

Recommended TypeScript type:

```ts
export type CriterionApplicability = 'spaces' | 'people' | 'tools';
```

Recommended C# enum:

```csharp
public enum CriterionApplicability
{
    Spaces,
    People,
    Tools
}
```

Storage may use either:

1. normalized table `criterion_applicability`, or
2. PostgreSQL array/jsonb column.

Prefer normalized storage if the existing schema is relational and query-heavy. Prefer array/jsonb only if Criteria are already stored in a document-like shape.

## Recommended Storage Model

Use a normalized join table.

```text
criteria
  id
  name
  description
  data_type
  created_at
  updated_at

criterion_applicability
  criterion_id
  resource_type
```

Where `resource_type` is constrained to:

- `spaces`
- `people`
- `tools`

Rationale:

- supports filtering efficiently,
- avoids fragile serialized string handling,
- allows future extension to dynamic resource types,
- keeps many-to-many semantics explicit.

## Future Extension

This should be designed so it can evolve from static values to dynamic resource types later.

Do not hardcode UI/business logic in a way that assumes only Spaces, People, and Tools can ever exist.

For now, expose only these three values because they match the current product model.
