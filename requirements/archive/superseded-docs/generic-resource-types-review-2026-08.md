# Review of the generic-resource-types branch — August 2026

Three reviews (legacy sweep, correctness/DRY/KISS, performance) over the unpushed work on
`claude/generic-resource-types-r9m82d` and its sibling commits in `orkyo-saas`,
`orkyo-community` and `orkyo-documentation`. Roughly seventy findings; the fixes are in the
seven commits from `91b9cb9` to `ce361e4`.

This file records what the reviews changed **about the design**, and the decisions that a
future reader would otherwise have to reconstruct from a diff. Individual fixes are in their
commit messages; only the load-bearing ones are repeated here.

---

## 1. The finding that mattered

Migration 1700 moved three behaviours out of type-key string comparisons and into
`resource_types` columns, and its header said why: "where a tenant can reach it". The SQL layer
used them. **Nothing above it did** — no model, no DTO, no endpoint, no TS interface — so every
layer that mattered still asked `key === 'space'`, and the codebase carried both mechanisms at
once.

That is the dual system the whole refactor exists to remove, reintroduced by the migration
meant to remove it. The flags are now model-, API- and UI-level (`25fd15f`), editable on
tenant-defined types, locked on system types.

**Lesson worth keeping:** a migration that adds a column "so the application can decide from
data" is half a change. The half that matters is the code that stops deciding from a string.

---

## 2. Decisions taken, with reasons

### 2.1 People are not a targetable resource type (UI)

A request's target slot holds exactly one resource per type — assigning a second replaces the
first, which is what makes "scheduled" checkable. The People section attaches *many* people to
one request. Both models were live on the same tab, so ticking "Person" in **Needs** and
choosing one name silently cancelled the rest of the crew.

The two models cannot share a tab. Staffing stays with the People section; the backend remains
able to express a person target, so this is a UI restriction, not a schema one. Revisit if a
target ever means "one or more".

### 2.2 Presets and the space export stay key-driven

The review wanted `WHERE key = 'space'` replaced with `has_geometry` everywhere. Wrong for
these two: a floorplan preset is a curated layout **of spaces**, and `has_geometry` asks "any
placeable type", which for a tenant with two of them has no single answer. Those sites name
*identity*, not behaviour. The rule for future readers:

> Convert a key comparison to a flag when the code is asking *what can this thing do*.
> Leave it when the code is asking *which specific type is this*.

### 2.3 The Requests conflict registry stays unwindowed

It is the most expensive query on that page. But the tree lists every request, so a window
would leave rows outside it silently unbadged — a wrong answer rather than a cheaper one. The
precondition for windowing it is paging the tree first, so the window and the visible rows
agree. Noted in place at the call site.

### 2.4 System types can be renamed (a widening)

Previously any update to a system type was rejected. Now the *naming* is the tenant's — "Space"
may be "Room" or "Salle" in their vocabulary, which is why 1750 added a plural at all — while
behaviour, key and lifecycle stay locked, because the built-in Spaces and People pages are
written against them and a tenant could break a page they cannot repair.

**This needs a release note when the branch ships.** Admins gain a capability.

### 2.5 Placeable resources cannot travel

Enforced centrally in `ResourceService`. A drawn shape belongs to one floorplan, and both the
implicit site-on-schedule and the working-hours adjustment find where work happens by looking
for the first resource that *cannot* travel. A placeable resource claiming it could would be
skipped by both. Unreachable while only `SpaceService` could create a space; reachable the
moment `/api/resources` could.

---

## 3. Corrections to unreleased migrations

1690–1750 had not shipped, so they were corrected in place (`91b9cb9`) rather than patched by a
later file. Consequence: **any database that ran the earlier versions must be rebuilt**, since
the recorded checksums no longer match and the trigger set genuinely differs. Local dev tenants
only — `dev.sh migrator` against a dropped-and-recreated tenant DB.

The substantive corrections:

- The search trigger fired on every `UPDATE resources`, including the unconditional
  `updated_at = NOW()` every repository write carries. A `WHEN` clause now limits it to the
  columns the document actually reads. **The guard's own risk is the mirror image** — add a
  column to the document, forget the guard, and it silently goes stale — so a test pins both
  directions.
- The sequence rebuilt every search document four times (1690 backfill, 1700's three backfill
  UPDATEs firing the trigger, 1710 reindex). Only the last can produce a correct document.
- A contract-classed migration was adding a column the new code reads unconditionally, which
  would 42703 in the window between deploy and migration. Split into 1685 (expand).

---

## 4. Open

- **The branch is unreleased.** `user-guide/settings/resource-types.md` and
  `user-guide/custom-resources.md` are deliberately still drafts in `orkyo-documentation`;
  publishing them is part of shipping, not of this work.
- **§2.4 needs a release note.**
- `SkillCatalog.SkillKind` was reported as orphaned and is not — a scaffold test reads it. It
  duplicates what `CapabilityFactory.TypesFor` expresses for the database; if they ever
  disagree, `TypesFor` is the one that reaches Postgres. Noted at the declaration.
