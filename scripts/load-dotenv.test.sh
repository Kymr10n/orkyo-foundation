#!/usr/bin/env bash
# Tests for load-dotenv.sh. Run: bash scripts/load-dotenv.test.sh
# Runs under bash (the shell the dev.sh scripts use) — the trailing-'=' case is the
# regression guard: it fails if the parser reverts to `IFS='=' read`.
set -uo pipefail

cd "$(dirname "$0")"
# shellcheck source=./load-dotenv.sh
source ./load-dotenv.sh

fail=0
assert_eq() { # name expected actual
  if [[ "$2" == "$3" ]]; then
    echo "ok   - $1"
  else
    echo "FAIL - $1: expected [$2] got [$3]"; fail=1
  fi
}

tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT
cat > "$tmp" <<'EOF'
# a comment line — must be skipped
ORKYO_MASTER_ENCRYPTION_KEY=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
CONN=Host=db;Port=5432;Pwd=abc=
DEFAULT_TENANT=demo                      # optional — inline comment + trailing spaces
BLANK=

  indented_is_not_a_key=nope
EOF

load_dotenv "$tmp"

assert_eq "trailing '=' preserved (base64 key)" \
  "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=" "${ORKYO_MASTER_ENCRYPTION_KEY:-}"
assert_eq "master key length is 44" "44" "${#ORKYO_MASTER_ENCRYPTION_KEY}"
assert_eq "internal + trailing '=' preserved" "Host=db;Port=5432;Pwd=abc=" "${CONN:-}"
assert_eq "inline comment stripped + rtrimmed" "demo" "${DEFAULT_TENANT:-}"
# Use ${x-UNSET} (no colon) so an exported-but-empty var is distinguished from a truly unset one.
assert_eq "empty value exported (not unset)" "" "${BLANK-UNSET}"
assert_eq "indented / non-key line skipped" "UNSET" "${indented_is_not_a_key-UNSET}"

if [[ "$fail" -eq 0 ]]; then
  echo "PASS: all load_dotenv assertions"
else
  echo "FAILURES in load_dotenv"
fi
exit "$fail"
