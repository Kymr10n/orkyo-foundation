# Scheduling integrity — specification and implementation plan

Status: **in progress**. Written 2026-10-28. Committed locally, not pushed: steps 0 to 4 and 7,
the product part of steps 5 and 6, and the user documentation. Open: the auto-bump workflow pin
in the products (needs step 4 on foundation main).

This document fixes five defects that an end-to-end test of optimistic concurrency (saas#299)
and a review of the demo seed found. The defects are in resource load checks, conflict
reporting, the recalculation of a site's schedule, the demo seed, and the package pins of the
products.

Four product decisions apply:

1. All five areas are fixed together, and this document tracks each step.
2. `ConcurrentCapacity` becomes a real allocation mode. `Exclusive` stays one booking at a time.
3. Finished work reports no conflicts. Its bookings still occupy the resource for other work.
4. A cancelled request occupies nothing.

## 1. Evaluation

| Area | Defect | Effect |
|---|---|---|
| Load | Every check of Fractional load adds all bookings that overlap the window. No check finds the peak at one instant. | Bookings that never run at the same time cause false `capacity_exceeded` conflicts. |
| Capacity | The Capacity field promises concurrent bookings. No check reads it. `ConcurrentCapacity` is rejected as "not yet supported". | A 12-place room reports an overlap for 2 bookings. |
| Load | The schedule write path books Fractional resources with no percent. The validator counts it as 0, Utilization and Insights count it as 100. | The conflict registry drops these bookings, and the two views disagree. |
| Conflicts | No check reads the request status. | Finished and cancelled work report conflicts and occupy resources. |
| Recalculation | A save of the scheduling settings recalculates windows that it read before its transaction. It writes them without a version check. | A drag during the save is lost. |
| Seed | The seed writes no `site_id` for jobs. All start times are full hours. The demo uses 600 people. | Insights counts most jobs at every site. The calendar looks artificial. |
| Pins | The products copy foundation's package pins by hand. | A local product build stops with NU1109 when foundation is ahead. |

## 2. Load model

Each booking has a **unit**, and each resource has a **limit**. A check finds the **peak** of
the units of the bookings at one instant, with half-open intervals `[start, end)`.

| Mode | Unit of a booking | Limit |
|---|---|---|
| `Exclusive` | 1 | 1 |
| `Fractional` | `allocation_percent`, or 100 when it is empty | `base_availability_percent` |
| `ConcurrentCapacity` | `allocation_units`, or 1 when it is empty | `capacity` |

A booking that ends at the time another booking starts does not overlap it. One class,
`ResourceLoad`, holds the rule. The assignment validator and the conflict registry use it. The
seed keeps a stricter rule: it adds all bookings that overlap a new one. A sum is never less
than the peak, so the seed never books past a limit. Utilization and Insights stay averages over time, because they measure how busy a
resource is. They do not check a limit.

An API caller still sends a percent when it books a Fractional resource. An empty percent
from the schedule path counts as the full resource.

## 3. Allocation modes

`ConcurrentCapacity` is valid only on resource types with geometry, because only these types
have a capacity. The edit dialog shows the mode only for these types. The Capacity help text
tells which mode uses the value. The draft overlay on the scheduler marks an overlap only for
`Exclusive` resources.

## 4. Conflict policy

- A conflict is reported only for live work. The effective status of the request is not
  `done` and not `cancelled`.
- The bookings of `done` work occupy the resource. Other work that overlaps them reports a
  conflict.
- The bookings of `cancelled` work occupy nothing. They do not show in the bookings, the
  utilization or the status of a resource.
- When two bookings exceed a limit together, both report the conflict.

## 5. Recalculation

A save of the scheduling settings locks the requests that it recalculates, in its own
transaction. A request that changed after the recalculation read it keeps its new window. The
save writes the other requests. This is the same lock-then-compare pattern as a request edit.

## 6. Demo seed

- Each job gets the `site_id` of its facility when the seed writes it. The unscheduled backlog
  stays without a site, by design.
- Start times use quarter hours inside the 06:00–14:00 shift. Durations stay full hours.
- The demo uses `--scale medium` (72 people). `--scale large` is for load tests.

## 7. Package pins

`scripts/ci/sync-product-pins.py` holds the rule: a product pin is never lower than
foundation's pin for the same package. The auto-bump workflow runs it to raise the pins.
`./dev.sh check` in a product runs it with `--check` against the sibling foundation checkout.

## 8. Decisions and open questions

Decided:

- Peak, not sum, for every limit check (section 2).
- An empty Fractional percent counts as 100 (section 2).
- `ConcurrentCapacity` only on types with geometry (section 3).
- Done work reports no conflicts but occupies. Cancelled work occupies nothing (section 4).
- The user's edit wins over a recalculation that read an older version (section 5).

Open: none.

Follow-ups, outside this plan:

- The auto-schedule solvers treat every resource as `Exclusive`. This is safe but leaves
  Fractional and concurrent resources under-used. CP-SAT `AddCumulative` can model them.
- A PUT treats an empty value as "no change", so a field cannot be cleared with `null`. This is
  an API contract question.

## 9. Implementation plan

Each step is one change set. The status of a step changes from planned to in progress,
implemented and committed. The work is tracked in one session, without pull requests.

| Step | Repo | Content | Status |
|---|---|---|---|
| 0 | foundation | This document | committed |
| 1 | foundation | Load model, `ConcurrentCapacity`, conflict policy: backend and frontend (sections 2–4) | committed |
| 2 | foundation | Recalculation lock (section 5). Builds on saas#299 | committed |
| 3 | foundation | Seed: `site_id`, quarter-hour starts (section 6) | committed |
| 4 | foundation | `sync-product-pins.py`, the auto-bump workflow uses it, `./dev.sh check` runs it (section 7) | committed |
| 5 | orkyo-community | Pins raised and demo docs use `medium` (committed). Auto-bump workflow pin moved to step 4 (needs step 4 on main) | in progress |
| 6 | orkyo-saas | Pins raised (committed). Auto-bump workflow pin moved to step 4 (needs step 4 on main) | in progress |
| 7 | foundation | CHANGELOG entries; user documentation (stations, planning model, conflicts) | committed |

The products get steps 1 to 3 through the release train.
