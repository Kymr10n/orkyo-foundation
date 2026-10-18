#!/usr/bin/env bash
# dev-common.sh — the part of ./dev.sh that is the same in every product.
#
# A product's dev.sh sets its configuration, sources this file, defines the
# product-specific commands, and ends with `dev_dispatch "$@"`. Everything below is
# what the SaaS and Community scripts carried as identical copies before 2026-09
# (helpers, the docker-compose plumbing, the host-process runners, the dispatcher).
#
# Required before sourcing:
#   ROOT_DIR             the product checkout (the caller's own directory)
#   PRODUCT_NAME         "SaaS" | "Community" (messages and URLs)
#   PRODUCT_REPO         "orkyo-saas" | "orkyo-community" (messages)
#   SEED_PROJECT_DIR     path of the seed CLI project
#   KEYCLOAK_MGMT_PORT   host port of Keycloak's management endpoint (tracks compose.local.yml)
# Provided by the product (called from here):
#   load_env             exports the derived runtime variables from .env
#   show_help            the command list (the seed examples differ per product)
#   cmd_up, cmd_infra, cmd_rebuild, cmd_migrator, cmd_doctor, print_stack_urls
#
# No defaults: a missing .env value fails loudly (repo rule). The shared .env loader is
# load-dotenv.sh, next to this file.

LOCAL_COMPOSE_FILE="$ROOT_DIR/compose.local.yml"
# Optional, gitignored per-developer overrides (e.g. a LAN hostname for phone
# testing). Merged after the base file when present; absent for normal localhost dev.
LOCAL_COMPOSE_OVERRIDE="$ROOT_DIR/compose.local.override.yml"
FRONTEND_ROOT="$ROOT_DIR/frontend"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

COMPOSE_CMD=(docker compose -f "$LOCAL_COMPOSE_FILE")
[[ -f "$LOCAL_COMPOSE_OVERRIDE" ]] && COMPOSE_CMD+=(-f "$LOCAL_COMPOSE_OVERRIDE")
COMPOSE_CMD+=(--env-file "$ROOT_DIR/.env")

# Shared .env loader (parses base64/`=`-bearing values correctly; see the helper's header).
# shellcheck source=/dev/null
source "$(dirname "${BASH_SOURCE[0]}")/load-dotenv.sh"

log()     { echo -e "${BLUE}[dev]${NC} $*"; }
success() { echo -e "${GREEN}[dev]${NC} $*"; }
warn()    { echo -e "${YELLOW}[dev]${NC} $*"; }
error()   { echo -e "${RED}[dev]${NC} $*" >&2; }

ensure_env() {
  if [[ ! -f "$ROOT_DIR/.env" ]]; then
    error ".env file not found"
    echo "Create it with: cp .env.template .env"
    exit 1
  fi
}

ensure_local_compose() {
  if [[ ! -f "$LOCAL_COMPOSE_FILE" ]]; then
    error "Local infra compose file not found: $LOCAL_COMPOSE_FILE"
    exit 1
  fi
}

sync_assets() {
  local sync_script="$ROOT_DIR/../orkyo-foundation/scripts/sync-assets.sh"
  if [[ -x "$sync_script" ]]; then
    log "Syncing brand assets from orkyo-foundation"
    "$sync_script"
  else
    warn "orkyo-foundation/scripts/sync-assets.sh not found — skipping asset sync"
    warn "Clone orkyo-foundation as a sibling of ${PRODUCT_REPO} and re-run"
  fi
}

check_env_or_confirm() {
  if ! "$ROOT_DIR/scripts/check-env.sh"; then
    echo ""
    read -r -p "Continue anyway? (y/N) " reply
    if [[ ! "$reply" =~ ^[Yy]$ ]]; then
      error "Aborted"
      exit 1
    fi
  fi
}

wait_for_url() {
  local url="$1"
  local description="$2"
  local retries=45

  log "Waiting for ${description}: ${url}"
  until curl -sf "$url" >/dev/null 2>&1; do
    retries=$((retries - 1))
    if [[ $retries -le 0 ]]; then
      error "${description} did not become healthy in time"
      exit 1
    fi
    printf '.'
    sleep 2
  done
  echo ""

  success "${description} is healthy"
}

wait_for_keycloak() {
  wait_for_url "http://localhost:${KEYCLOAK_MGMT_PORT}/health/ready" "Keycloak"
}

print_infra_urls() {
  echo "Postgres: localhost:${POSTGRES_PORT}"
  echo "Valkey:    localhost:${VALKEY_PORT}"
  echo "Keycloak: http://localhost:${KEYCLOAK_PORT}"
  echo "MailHog:  http://localhost:${MAILHOG_UI_PORT}"
}

cmd_down() {
  ensure_local_compose
  "${COMPOSE_CMD[@]}" down
  success "Stack stopped"
}

cmd_restart() {
  cmd_down
  cmd_up
}

cmd_logs() {
  ensure_local_compose
  shift || true
  "${COMPOSE_CMD[@]}" logs -f "$@"
}

cmd_status() {
  ensure_local_compose
  "${COMPOSE_CMD[@]}" ps
}

run_dotnet_project() {
  local project_dir="$1"
  shift
  load_env
  cd "$project_dir"
  dotnet run -- "$@"
}

cmd_reset() {
  ensure_local_compose
  warn "This removes local Docker volumes for the stack."
  read -r -p "Proceed? (y/N) " reply
  if [[ ! "$reply" =~ ^[Yy]$ ]]; then
    echo "Cancelled"
    exit 0
  fi

  "${COMPOSE_CMD[@]}" down -v
  success "Volumes removed"
}

cmd_api() {
  run_dotnet_project "$ROOT_DIR/backend/api"
}

cmd_worker() {
  run_dotnet_project "$ROOT_DIR/backend/worker"
}

cmd_seed() {
  run_dotnet_project "$SEED_PROJECT_DIR" "$@"
}

cmd_frontend() {
  load_env

  local target_dir="$FRONTEND_ROOT"
  if [[ ! -f "$target_dir/package.json" ]]; then
    error "No frontend application found in ${PRODUCT_REPO}/frontend"
    error "Frontend application must be present in ${PRODUCT_REPO}/frontend (current: $target_dir)"
    exit 1
  fi

  cd "$target_dir"
  npm run dev -- --host 0.0.0.0 --port "${FRONTEND_PORT}"
}

# ── check: the CI sequence, locally ────────────────────────────────────────────
# Runs what the backend-ci job runs, in its order, against a fresh Postgres container per
# run and per repo (CI gets a new one every run too; tests that count rows or expect "no
# row yet" fail against a server that kept the previous run's data). Test fixtures pick it
# up through the CI switch they already have (CI=true + ConnectionStrings__Postgres), so no
# test code knows about this script.
# `--fast` runs only the architecture and convention tests, which need no database; the
# pre-push hook uses it. The image pin is the smoke-tests job's.
TEST_POSTGRES_IMAGE="postgres:16-alpine@sha256:cf78e76683b9ca8c5733cbbdce6c9262b45b6767934dd0a95e671f9a0fc20685"
# One server per repo: the suites create databases with the same names (control_plane,
# tenant_test) from different migration sets, so they must not share a server.
case "$PRODUCT_REPO" in
  orkyo-foundation) TEST_POSTGRES_PORT=5440 ;;
  orkyo-saas)       TEST_POSTGRES_PORT=5441 ;;
  orkyo-community)  TEST_POSTGRES_PORT=5442 ;;
  *) TEST_POSTGRES_PORT=5449 ;;
esac
TEST_POSTGRES_CONTAINER="orkyo-test-postgres-${PRODUCT_REPO#orkyo-}"
FAST_TEST_FILTER="(FullyQualifiedName~.Architecture.|FullyQualifiedName~ConventionContract|FullyQualifiedName~PublicEndpointTenantSkip)&FullyQualifiedName!~RouteInventory&FullyQualifiedName!~AuthorizationContract"

ensure_test_postgres() {
  docker rm -f "$TEST_POSTGRES_CONTAINER" >/dev/null 2>&1 || true
  log "Starting a fresh ${TEST_POSTGRES_CONTAINER} on 127.0.0.1:${TEST_POSTGRES_PORT}"
  docker run -d --name "$TEST_POSTGRES_CONTAINER" -p "127.0.0.1:${TEST_POSTGRES_PORT}:5432" \
    -e POSTGRES_PASSWORD=postgres "$TEST_POSTGRES_IMAGE" -c max_connections=300 \
    -c fsync=off -c synchronous_commit=off -c full_page_writes=off >/dev/null
  local i
  for i in $(seq 1 30); do
    docker exec "$TEST_POSTGRES_CONTAINER" pg_isready -U postgres -q 2>/dev/null && return 0
    sleep 1
  done
  error "${TEST_POSTGRES_CONTAINER} did not become ready"
  exit 1
}

check_step() {
  local job="$1" name="$2"
  shift 2
  log "[${job}] ${name}"
  "$@" || { error "FAILED [${job}] ${name}"; exit 1; }
}

frontend_changed() {
  [[ -d "$FRONTEND_ROOT" ]] || return 1
  local base
  base=$(git -C "$ROOT_DIR" merge-base HEAD origin/main 2>/dev/null) || return 0
  ! git -C "$ROOT_DIR" diff --quiet "$base" -- frontend
}

# Leave nothing running: the test server is fresh per run anyway, and the MSBuild and
# Roslyn compiler servers would otherwise idle for 10-15 minutes holding hundreds of MB.
check_cleanup() {
  docker rm -f "$TEST_POSTGRES_CONTAINER" >/dev/null 2>&1 || true
  dotnet build-server shutdown >/dev/null 2>&1 || true
}

cmd_check() {
  trap check_cleanup EXIT
  local fast=false
  [[ "${1:-}" == "--fast" ]] && fast=true
  local sln
  sln=$(find "$ROOT_DIR" -maxdepth 1 -name '*.slnx' | head -1)
  [[ -n "$sln" ]] || { error "no .slnx in $ROOT_DIR"; exit 1; }
  local sql_dir="$ROOT_DIR/backend/migrations/sql"
  [[ -d "$ROOT_DIR/backend/migrations-foundation/sql" ]] && sql_dir="$ROOT_DIR/backend/migrations-foundation/sql"
  local ci_scripts="$(dirname "${BASH_SOURCE[0]}")/ci"
  cd "$ROOT_DIR"

  if $fast; then
    check_step backend-ci "build" dotnet build "$sln" -c Release -warnaserror --nologo -v q
    check_step backend-ci "architecture tests (no database)" dotnet test "$sln" -c Release --no-build --nologo --filter "$FAST_TEST_FILTER"
    success "fast check passed"
    return 0
  fi

  if [[ -n "$(find "$ROOT_DIR/backend" -maxdepth 3 -name packages.lock.json -print -quit)" ]]; then
    check_step backend-ci "restore (locked)" dotnet restore "$sln" --locked-mode
  else
    check_step backend-ci "restore" dotnet restore "$sln"
  fi
  check_step backend-ci "format" dotnet format "$sln" --verify-no-changes --no-restore
  check_step backend-ci "build" dotnet build "$sln" -c Release --no-restore -warnaserror --nologo -v q
  check_step audit-secrets "migration headers" bash "$ci_scripts/lint-migration-headers.sh" "$sql_dir"
  check_step template-sync "dead registrations" python3 "$ROOT_DIR/scripts/ci/check-dead-registrations.py" --strict

  ensure_test_postgres
  export CI=true
  export ConnectionStrings__Postgres="Host=localhost;Port=${TEST_POSTGRES_PORT};Database=postgres;Username=postgres;Password=postgres"
  check_step backend-ci "tests" dotnet test "$sln" -c Release --no-build --nologo

  if frontend_changed; then
    check_step frontend-ci "lint" npm --prefix "$FRONTEND_ROOT" run lint
    check_step frontend-ci "typecheck" npm --prefix "$FRONTEND_ROOT" run typecheck
    check_step frontend-ci "tests" npm --prefix "$FRONTEND_ROOT" test
  else
    log "[frontend-ci] skipped: no frontend changes against origin/main"
  fi
  success "check passed — this is what CI runs"
}

dev_dispatch() {
  local command="${1:-help}"

  case "$command" in
    up) cmd_up ;;
    down) cmd_down ;;
    restart) cmd_restart ;;
    rebuild) cmd_rebuild ;;
    logs) cmd_logs "$@" ;;
    status) cmd_status ;;
    reset) cmd_reset ;;
    infra) cmd_infra ;;
    migrator) cmd_migrator ;;
    api) cmd_api ;;
    worker) cmd_worker ;;
    seed) shift; cmd_seed "$@" ;;
    frontend) cmd_frontend ;;
    doctor) cmd_doctor ;;
    check) shift; cmd_check "$@" ;;
    help|-h|--help) show_help ;;
    *)
      error "Unknown command: $command"
      show_help
      exit 1
      ;;
  esac
}
