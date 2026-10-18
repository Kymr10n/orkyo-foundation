#!/usr/bin/env bash
# scripts/check.sh — foundation's `./dev.sh check`: the backend-ci sequence, locally.
# The products reach the same function through their dev.sh; see dev-common.sh.
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
PRODUCT_NAME="Foundation"; PRODUCT_REPO="orkyo-foundation"; SEED_PROJECT_DIR=""; KEYCLOAK_MGMT_PORT=""
# shellcheck source=dev-common.sh
source "$ROOT_DIR/scripts/dev-common.sh"
cmd_check "$@"
