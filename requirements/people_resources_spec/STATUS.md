# People Resources — Implementation Status

**Single source of truth for progress on this initiative.**
The implementing agent updates this file at the end of every phase and at any
meaningful checkpoint (blocker found, scope change, partial PR merged).

> Rules for the implementing agent:
> 1. Read this file before starting any work in a session.
> 2. After finishing a phase, flip its checkbox to ✅, fill in the date, the
>    commit / PR link, the test names added, and any deviations from
>    [02_people_resources_implementation_plan.md](02_people_resources_implementation_plan.md).
> 3. If a phase is paused or blocked, mark it 🟡 and write the blocker under
>    "Open issues / blockers".
> 4. If a decision in [01_people_resources_specification.md §18](01_people_resources_specification.md)
>    has to change, record it under "Decision changes" with rationale **before**
>    coding around it.
> 5. Never delete history from this file — append, don't rewrite.

---

## Summary

| Phase | Title | Status | Date | Commit / PR |
|------:|-------|:------:|------|-------------|
| 0 | Kickoff & baseline | ⬜ | | |
| 1 | Schema deltas | ⬜ | | |
| 2 | Off-time + typed capability checks | ⬜ | | |
| 3 | Validation dry-run endpoint | ⬜ | | |
| 4 | Person profile API | ⬜ | | |
| 5 | Frontend: People page | ⬜ | | |
| 6 | Request dialog: People assignment | ⬜ | | |
| 7 | Utilization page: People grid | ⬜ | | |
| 8 | Polish, docs, release notes | ⬜ | | |

Legend: ⬜ not started · 🟡 in progress / blocked · ✅ done

---

## Phase logs

### Phase 0 — Kickoff & baseline
- Status: ⬜
- Baseline commit:
- Notes:

### Phase 1 — Schema deltas
- Status: ⬜
- Migration file:
- Columns / tables added:
- Tests added:
- Notes:

### Phase 2 — Off-time + typed capability checks
- Status: ⬜
- Files changed:
- Tests added:
- Notes:

### Phase 3 — Validation dry-run endpoint
- Status: ⬜
- Endpoint:
- Reason codes implemented:
- Tests added:
- Notes:

### Phase 4 — Person profile API
- Status: ⬜
- Endpoints:
- Tests added:
- Notes:

### Phase 5 — Frontend: People page
- Status: ⬜
- Route + nav wiring:
- Components added:
- Tests added:
- Notes:

### Phase 6 — Request dialog: People assignment
- Status: ⬜
- Files changed:
- Tests added:
- Notes:

### Phase 7 — Utilization page: People grid
- Status: ⬜
- Files changed:
- Tests added:
- Notes:

### Phase 8 — Polish, docs, release notes
- Status: ⬜
- Docs touched:
- Release notes line:

---

## Decision changes

Append entries here whenever a decision in 01 §18 has to be revised. Format:

```
- YYYY-MM-DD — <decision id / topic>
  Old: <what 01 §18 said>
  New: <what we are doing instead>
  Why: <rationale>
  Phase impact: <which phases need to be redone or amended>
```

(none yet)

---

## Open issues / blockers

Append entries here when a phase is paused. Remove the entry (and add a closing
note in the phase log) once it's resolved.

(none yet)

---

## Deferred items (tracked, not implemented in v1)

Mirrors 01 §18 / 02 "Deferred" — listed here so future sessions can find them:

- User invitation flow
- Group default capabilities
- Group-scoped absences
- Regional holiday provider
- Drag-and-drop assignments
- `ConcurrentCapacity` allocation mode
