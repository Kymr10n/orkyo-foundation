# Archived 2026-05-14

These documents described the original "phased dual-write with feature
flags, parallel capability tables, and retained `spaceId`" approach.

They were superseded by the current pack at the parent directory after
a planning discussion that settled on:

- Clean break — no legacy retention, no synonyms, no feature flags.
- Parallel build first, then atomic cutover (instead of long dual-write).
- `criteria` stays as `criteria` (no rename); applicability tagging added.
- Space IS a Resource via shared uuid (no separate `spaces.resource_id`).
- `requests.space_id` is dropped; `resource_assignments` is canonical.

Kept for historical context only. Do not implement from these files.
