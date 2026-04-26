#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
# Sync brand assets from the single source of truth (foundation/assets/) to all
# consumers across the multi-repo platform:
#
#   Destinations:
#     1. ../orkyo-saas/frontend/public/           (favicon pack)
#     2. keycloak/themes/orkyo/login/resources/img/orkyo-logo.png
#     3. ../orkyo-community/frontend/public/      (favicon pack, when repo exists)
#
# Usage:
#   ./scripts/sync-assets.sh [--check]
#   --check   Dry-run: report differences without copying
#
# Run from the orkyo-foundation root, or from any location (uses script dir).
# ──────────────────────────────────────────────────────────────────────────────
set -euo pipefail

cd "$(dirname "$0")/.."

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

CHECK_ONLY=false
[[ "${1:-}" == "--check" ]] && CHECK_ONLY=true

ASSET_DIR="assets/orkyo-favicon-pack"
DIRTY=false

sync_file() {
    local src="$1"
    local dst="$2"

    if [[ ! -f "$src" ]]; then
        echo -e "${RED}  ✗ Source missing: $src${NC}"
        return 1
    fi

    if [[ -L "$dst" ]]; then
        if $CHECK_ONLY; then
            echo -e "${YELLOW}  ⚠ Symlink should be a real file: $dst${NC}"
            DIRTY=true
            return 0
        fi
        rm "$dst"
    fi

    if [[ ! -f "$dst" ]] || ! cmp -s "$src" "$dst"; then
        DIRTY=true
        if $CHECK_ONLY; then
            echo -e "${YELLOW}  ⚠ Out of sync: $dst${NC}"
        else
            mkdir -p "$(dirname "$dst")"
            cp "$src" "$dst"
            echo -e "${GREEN}  ✓ Synced: $dst${NC}"
        fi
    fi
}

sync_favicon_pack() {
    local dest_dir="$1"
    # Destination files are gitignored in the consuming repo — source of truth
    # is foundation/assets/. Run this script before build or local dev startup.
    if [[ ! -d "$(dirname "$dest_dir")" ]]; then
        echo -e "${YELLOW}  ⚠ Skipped (repo not present): $dest_dir${NC}"
        return 0
    fi
    echo "  → $dest_dir/"
    for src in "$ASSET_DIR"/*.png "$ASSET_DIR"/favicon.ico; do
        [[ -f "$src" ]] || continue
        sync_file "$src" "$dest_dir/$(basename "$src")"
    done
}

echo "Syncing assets from $ASSET_DIR ..."

echo ""
echo "── 1. Keycloak login theme logo ────────────────────────────────────────────"
sync_file "$ASSET_DIR/orkyo-icon-master-transparent.png" \
          "keycloak/themes/orkyo/login/resources/img/orkyo-logo.png"

echo ""
echo "── 2. SaaS frontend public icons ───────────────────────────────────────────"
sync_favicon_pack "../orkyo-saas/frontend/public"

echo ""
echo "── 3. Community frontend public icons ──────────────────────────────────────"
sync_favicon_pack "../orkyo-community/frontend/public"

echo ""
if $DIRTY; then
    if $CHECK_ONLY; then
        echo -e "${YELLOW}⚠  Assets are out of sync. Run ./scripts/sync-assets.sh to fix.${NC}"
        exit 1
    else
        echo -e "${GREEN}✅ Assets synced successfully.${NC}"
    fi
else
    echo -e "${GREEN}✅ All assets already up to date.${NC}"
fi
