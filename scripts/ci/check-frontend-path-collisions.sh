#!/usr/bin/env bash
# check-frontend-path-collisions.sh — flag files that exist at the SAME relative
# path under both orkyo-foundation/frontend/src and orkyo-saas/frontend/src.
#
# A shared path is the fingerprint of a copy-paste even after the copies drift in
# content (so a content hash would miss it). The placement rule (foundation
# CLAUDE.md) says shared feature code belongs in @kymr10n/foundation, not
# duplicated into a product. Genuinely-independent same-named files (entrypoints,
# per-app store) live in frontend-path-collision-allowlist.txt.
#
# Usage: check-frontend-path-collisions.sh <workspace-dir>
#   <workspace-dir> contains sibling checkouts orkyo-foundation, orkyo-saas.
#   Defaults to the parent of this repo.
#
# See orkyo-infra/docs/optimization-plan-2026-07.md §Guardrails (G8).
set -Eeuo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
workspace="${1:-$(dirname "$repo_root")}"
allowlist="$repo_root/scripts/ci/frontend-path-collision-allowlist.txt"

foundation_src="$workspace/orkyo-foundation/frontend/src"
saas_src="$workspace/orkyo-saas/frontend/src"

for d in "$foundation_src" "$saas_src"; do
  if [[ ! -d "$d" ]]; then
    echo "::warning::$d not found — skipping path-collision check." >&2
    exit 0
  fi
done

list_src() {
  find "$1" -type f \( -name '*.ts' -o -name '*.tsx' \) \
    ! -name '*.test.*' ! -name '*.spec.*' \
    | sed "s|^$1/||" | sort
}

# Allowed paths = non-comment, non-blank lines from the allowlist.
allowed="$(grep -vE '^\s*(#|$)' "$allowlist" | sort -u || true)"

collisions="$(comm -12 <(list_src "$foundation_src") <(list_src "$saas_src"))"

# Subtract the allowlist.
offenders="$(comm -23 <(printf '%s\n' "$collisions" | sort -u) <(printf '%s\n' "$allowed"))"
offenders="$(printf '%s\n' "$offenders" | grep -vE '^\s*$' || true)"

if [[ -n "$offenders" ]]; then
  while IFS= read -r path; do
    echo "::error file=frontend/src/${path}::exists in BOTH orkyo-foundation and orkyo-saas frontend/src. Apply the placement rule: delete the saas copy and import from @kymr10n/foundation, or add it to frontend-path-collision-allowlist.txt if genuinely distinct." >&2
  done <<< "$offenders"
  exit 1
fi
echo "check-frontend-path-collisions: OK (no un-allowlisted shared paths)"
