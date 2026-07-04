#!/usr/bin/env bash
# Shared .env loader for the edition dev.sh scripts. Source this file, don't execute it.
#
# Splits each KEY=VALUE line on the FIRST '=' and exports the value verbatim, so values
# that contain or end with '=' (e.g. base64 keys like a 32-byte AES key → "…AAA=") survive.
# The naive `while IFS='=' read -r key value` idiom strips a trailing '=' in bash (because
# '=' is the field separator), which corrupts base64 padding — that bug is exactly what this
# helper exists to avoid.
#
# Also strips an inline "# comment" and trailing whitespace, matching the .env conventions
# used across the editions (the SaaS .env annotates optional keys with inline comments).

load_dotenv() {
  local file="$1" line key value
  [[ -f "$file" ]] || { echo "load_dotenv: file not found: $file" >&2; return 1; }
  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ "$line" =~ ^[A-Za-z_][A-Za-z0-9_]*= ]] || continue
    key="${line%%=*}"
    value="${line#*=}"
    value="${value%%#*}"                          # strip inline comment
    value="${value%"${value##*[![:space:]]}"}"    # rtrim whitespace
    export "$key=$value"
  done < "$file"
}
