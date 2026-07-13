#!/usr/bin/env bash
# check-synced-files.sh — verify that intentionally-synced files (see
# synced-files.manifest) are byte-identical across repos. Fails on drift so a
# sync PR can be opened. No runtime coupling — purely a review-time ratchet.
#
# Usage: check-synced-files.sh <workspace-dir>
#   <workspace-dir> contains sibling checkouts named orkyo-foundation,
#   orkyo-saas, orkyo-community. Defaults to the parent of this repo.
#
# See orkyo-infra/docs/optimization-plan-2026-07.md §Guardrails (G4).
set -Eeuo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
workspace="${1:-$(dirname "$repo_root")}"
manifest="$repo_root/scripts/ci/synced-files.manifest"

if [[ ! -f "$manifest" ]]; then
  echo "::error::manifest not found: $manifest" >&2
  exit 1
fi

fail=0
missing=0
while IFS=$'\t' read -r path source consumers; do
  # Skip comments / blanks.
  [[ -z "${path// }" || "${path:0:1}" == "#" ]] && continue

  src_file="$workspace/$source/$path"
  if [[ ! -f "$src_file" ]]; then
    echo "::error::source missing: $source/$path (checkout absent in $workspace?)" >&2
    missing=1
    continue
  fi

  IFS=',' read -ra consumer_list <<< "$consumers"
  for consumer in "${consumer_list[@]}"; do
    dst_file="$workspace/$consumer/$path"
    if [[ ! -f "$dst_file" ]]; then
      echo "::error file=${path}::$consumer is missing $path (source: $source). Copy it from $source or remove the manifest row." >&2
      fail=1
    elif ! cmp -s "$src_file" "$dst_file"; then
      echo "::error file=${path}::$consumer/$path has drifted from $source/$path. Copy the source version and open a sync PR, or remove the manifest row if the fork is intentional." >&2
      fail=1
    fi
  done
done < "$manifest"

if [[ "$missing" -ne 0 ]]; then
  echo "::warning::one or more source checkouts were absent — drift could not be fully checked." >&2
fi
if [[ "$fail" -ne 0 ]]; then
  exit 1
fi
echo "check-synced-files: OK (all manifest rows byte-identical)"
