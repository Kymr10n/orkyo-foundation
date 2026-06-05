# People Resources — Kickoff Prompt for the Implementing Agent

Paste this into a new Sonnet session to start (or resume) the work.

---

You are implementing the **People Resources v1** initiative for `orkyo-foundation`.

**Read first, in this order:**
1. [STATUS.md](STATUS.md) — current progress. Resume from the next ⬜ phase.
2. [01_people_resources_specification.md](01_people_resources_specification.md), specifically **§18** (codebase reality + decisions). It overrides earlier sections.
3. [02_people_resources_implementation_plan.md](02_people_resources_implementation_plan.md) — the phased plan you will execute.

**Hard rules:**
- Do not recreate generic resource infrastructure. The resource model (`resources`, `resource_groups`, `resource_capabilities`, `resource_assignments`, `off_times`, `scheduling_settings`) already exists; reuse it.
- Every behavioral change ships with tests in the same PR.
- Update `STATUS.md` after every phase (and at any meaningful checkpoint). Treat it as the single source of truth for progress; never rewrite history, only append.
- Migrations carry a `-- @migration-class:` header.
- Do not break existing public contracts on `/api/resources*`, `/api/utilization`, `/api/criteria*`.
- Anything in the "Deferred" list in 02 is **out of scope** — do not implement it. Open a new spec pack if it becomes needed.

**Workflow per phase:**
1. Mark the phase 🟡 in STATUS.md.
2. Implement.
3. Run `dotnet test` (foundation slnx) and `pnpm -C frontend test`.
4. Mark the phase ✅, fill in the row in the summary table and the per-phase log section (date, commit, files, tests).
5. If you hit a blocker or need a decision change, follow the rules at the top of STATUS.md before changing course.

Start with the next ⬜ phase in STATUS.md. If all phases are ⬜, start at Phase 0.
