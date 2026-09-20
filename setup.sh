#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

# The log helpers, check_cmd and the hook installation are identical in all three repos.
# shellcheck source=scripts/setup-common.sh
source "scripts/setup-common.sh"

# Foundation requires pre-commit: its commit-msg hook enforces the Docs-impact trailer.
setup_install_git_hooks error

log "Checking prerequisites"
check_cmd dotnet
check_cmd node
check_cmd npm

log "Restoring backend dependencies"
dotnet restore Orkyo.Foundation.slnx

log "Installing frontend dependencies"
cd frontend && npm ci && cd ..

success "Setup complete"
