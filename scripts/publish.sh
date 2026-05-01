#!/usr/bin/env bash
# publish.sh <version>
#
# Tags the current main HEAD as v<version> and pushes it, triggering
# the foundation CI publish job (NuGet + npm packages + GitHub Release).
#
# Run this BEFORE scripts/release.sh <version> in orkyo-saas.
#
# Usage:
#   scripts/publish.sh 1.2.0

set -euo pipefail
cd "$(dirname "$0")/.."

# ── Colours ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
BOLD='\033[1m'
NC='\033[0m'

log()     { echo -e "${BLUE}[publish]${NC} $*"; }
success() { echo -e "${GREEN}[publish]${NC} $*"; }
warn()    { echo -e "${YELLOW}[publish]${NC} $*"; }
error()   { echo -e "${RED}[publish]${NC} $*" >&2; }
die()     { error "$*"; exit 1; }

# ── Argument validation ───────────────────────────────────────────────────────
VERSION="${1:-}"
if [[ -z "$VERSION" ]]; then
  die "Usage: scripts/publish.sh <version>  (e.g. 1.2.0)"
fi
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  die "Version must be X.Y.Z semver (got: '$VERSION')"
fi

TAG="v${VERSION}"

# ── Git state checks ──────────────────────────────────────────────────────────
CURRENT_BRANCH=$(git rev-parse --abbrev-ref HEAD)
if [[ "$CURRENT_BRANCH" != "main" ]]; then
  die "Must be on main branch (currently on: $CURRENT_BRANCH)"
fi

if ! git diff --quiet || ! git diff --cached --quiet; then
  log "Staging and committing working tree changes..."
  git add -A
  git commit -m "chore: release v${VERSION}"
fi

log "Fetching latest from origin..."
git fetch --quiet --tags origin main

# Allow HEAD to be ahead of origin/main (e.g. after an auto-commit); only
# fail if we are BEHIND (would overwrite remote work).
if ! git merge-base --is-ancestor origin/main HEAD; then
  die "Local main is behind origin/main — run: git pull --rebase"
fi

# ── Check for tag collision (inform only — act after confirmation) ────────────
LOCAL_TAG_EXISTS=false
REMOTE_TAG_EXISTS=false
if git tag --list "$TAG" | grep -q "$TAG"; then
  LOCAL_TAG_EXISTS=true
fi
if git ls-remote --exit-code --tags origin "$TAG" > /dev/null 2>&1; then
  REMOTE_TAG_EXISTS=true
fi

# ── Confirmation prompt ───────────────────────────────────────────────────────
HEAD_SHA=$(git rev-parse HEAD)
echo ""
echo -e "  ${BOLD}Publishing foundation packages${NC}"
echo ""
echo -e "    Version : ${BOLD}${VERSION}${NC}"
echo -e "    Tag     : ${BOLD}${TAG}${NC}"
echo -e "    SHA     : ${BOLD}${HEAD_SHA}${NC}"
echo ""
echo -e "  This will:"
echo "    1. Create and push tag $TAG on current main HEAD"
echo "    2. Trigger CI: pack + push NuGet packages, publish npm package, create GitHub Release"
if [[ "$LOCAL_TAG_EXISTS" == "true" || "$REMOTE_TAG_EXISTS" == "true" ]]; then
  warn "Tag $TAG already exists and will be reassigned to this commit."
fi
echo ""
read -r -p "Publish foundation $VERSION? [y/N] " CONFIRM
if [[ "$CONFIRM" != "y" && "$CONFIRM" != "Y" ]]; then
  log "Aborted."
  exit 0
fi

# ── Push commits (if ahead) ───────────────────────────────────────────────────
AHEAD=$(git rev-list origin/main..HEAD --count)
if [[ "$AHEAD" -gt 0 ]]; then
  log "Pushing ${AHEAD} commit(s) to origin/main..."
  git push origin main
fi

# ── Delete old tag (local + remote) immediately before re-creating ────────────
if git ls-remote --exit-code --tags origin "$TAG" > /dev/null 2>&1; then
  log "Removing remote tag $TAG..."
  git push origin ":refs/tags/$TAG"
fi

log "Tagging ${BOLD}${TAG}${NC} at ${HEAD_SHA}..."
git tag -f "$TAG"

log "Pushing tag to origin..."
git push origin "$TAG"

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
success "Tag ${BOLD}${TAG}${NC} pushed — foundation publish triggered."
echo ""
echo -e "  ${BOLD}Monitor publish job:${NC}"
echo "    https://github.com/Kymr10n/orkyo-foundation/actions"
echo ""
echo -e "  ${BOLD}Once the publish job completes, run in orkyo-saas:${NC}"
echo "    scripts/release.sh ${VERSION}"
echo ""
