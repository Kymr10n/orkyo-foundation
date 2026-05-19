# Orkyo — Criteria Registry and People Skill Assignment UX

## Purpose

This package specifies a UX and implementation change for Orkyo's criteria/skills handling.

The current implementation exposes a `People → Skills` tab that behaves like a secondary criteria management area. This creates unclear ownership because criteria are already globally managed under `Settings → Criteria`.

The target architecture is:

- `Settings → Criteria` is the authoritative global registry for all reusable criteria.
- Resource modules such as `People`, `Spaces`, and later `Tools` only assign applicable criteria to concrete resources.
- A person skill is not a separate domain concept. It is a criterion value assigned to a person.

## Files

1. `01-functional-specification.md`
2. `02-implementation-plan.md`
3. `03-acceptance-criteria.md`
4. `04-copilot-execution-prompt.md`
