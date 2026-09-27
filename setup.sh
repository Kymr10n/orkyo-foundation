#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m'

log()     { echo -e "${BLUE}[setup]${NC} $*"; }
success() { echo -e "${GREEN}[setup]${NC} $*"; }
error()   { echo -e "${RED}[setup]${NC} $*" >&2; }

check_cmd() {
  if command -v "$1" >/dev/null 2>&1; then
    success "$1 found"
  else
    error "$1 not found"
    exit 1
  fi
}

log "Installing git hooks"
# pre-commit installs into .git/hooks, which git ignores whenever core.hooksPath is set.
# An earlier version of this script pointed core.hooksPath at .githooks/ and so silently
# disabled every pre-commit hook, including the commit-msg docs-impact check. Clear it first.
if git config --get core.hooksPath >/dev/null 2>&1; then
  git config --unset core.hooksPath
  log "Cleared core.hooksPath; pre-commit owns the hooks now"
fi

# Foundation requires pre-commit: its commit-msg hook enforces the Docs-impact trailer.
if command -v pre-commit >/dev/null 2>&1; then
  pre-commit install --install-hooks
  success "git hooks installed (pre-commit, commit-msg, pre-push)"
else
  error "pre-commit not found — run: pip install pre-commit && ./setup.sh"
  exit 1
fi

log "Checking prerequisites"
check_cmd dotnet
check_cmd node
check_cmd npm

log "Restoring backend dependencies"
dotnet restore Orkyo.Foundation.slnx

log "Installing frontend dependencies"
cd frontend && npm ci && cd ..

success "Setup complete"
