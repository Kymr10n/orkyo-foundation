#!/usr/bin/env bash
# setup-common.sh — the part of ./setup.sh that is the same in every repo.
#
# A repo's setup.sh sources this file, calls setup_install_git_hooks, then does its own
# prerequisite checks and bootstrap. Everything below is what the SaaS and Community
# scripts carried as identical copies before 2026-09: the coloured log helpers, check_cmd,
# and the pre-commit hook installation.
#
# Sourcing (from the repo root, as setup.sh runs there):
#   # shellcheck source=/dev/null
#   source "$(dirname "$0")/../orkyo-foundation/scripts/setup-common.sh"
#   setup_install_git_hooks error
#
# The one real difference between the products is what happens when pre-commit is absent:
# SaaS warns and continues, Community stops. That fork is the argument.
#
# No defaults: setup_install_git_hooks requires its argument (repo rule).

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log()     { echo -e "${BLUE}[setup]${NC} $*"; }
success() { echo -e "${GREEN}[setup]${NC} $*"; }
warn()    { echo -e "${YELLOW}[setup]${NC} $*"; }
error()   { echo -e "${RED}[setup]${NC} $*" >&2; }

check_cmd() {
  if command -v "$1" >/dev/null 2>&1; then
    success "$1 found"
  else
    error "$1 not found"
    exit 1
  fi
}

# setup_install_git_hooks <on-missing>
#   on-missing = warn   pre-commit is optional; say so and continue
#   on-missing = error  pre-commit is required; stop the setup
setup_install_git_hooks() {
  local on_missing="${1:?\"warn\" or \"error\" expected as \$1}"
  case "$on_missing" in
    warn|error) ;;
    *) error "setup_install_git_hooks: unknown mode '$on_missing' (expected warn or error)"; exit 1 ;;
  esac

  log "Installing git hooks"
  # pre-commit installs into .git/hooks, which git ignores whenever core.hooksPath is set.
  # An earlier version of this script pointed core.hooksPath at .githooks/ and so silently
  # disabled every pre-commit hook, including the commit-msg docs-impact check. Clear it first.
  if git config --get core.hooksPath >/dev/null 2>&1; then
    git config --unset core.hooksPath
    log "Cleared core.hooksPath; pre-commit owns the hooks now"
  fi

  if command -v pre-commit >/dev/null 2>&1; then
    pre-commit install --install-hooks
    success "git hooks installed (pre-commit, commit-msg, pre-push)"
  elif [[ "$on_missing" == "warn" ]]; then
    warn "pre-commit not found — run: pip install pre-commit && pre-commit install"
  else
    error "pre-commit not found — run: pip install pre-commit && ./setup.sh"
    exit 1
  fi
}
