# Implementation Pack: People Utilization Timeline Segments

This pack contains a specification and implementation plan for refactoring the Orkyo People utilization grid from a background heatmap to read-only aggregated timeline segments.

Files:

- `SPEC_people_utilization_timeline_segments.md`
- `PLAN_people_utilization_timeline_segments.md`

Recommended use:

1. Give both files to Claude/Copilot.
2. Ask it to first inspect the current implementation and produce a short architecture note.
3. Then implement phase-by-phase, starting with segment model and tests.
4. Keep People timeline segments non-draggable and non-resizable.
