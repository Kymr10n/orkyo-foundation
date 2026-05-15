#!/usr/bin/env bash
# Run Foundation + Community + SaaS test suites in sequence.
#
# When to run: before merging any Foundation change that alters the signature
# of a service registered in FoundationWebApplicationFactory, or the public
# shape of an endpoint Map* function. This catches downstream DI graph and
# route-registration regressions that the Foundation test suite alone cannot
# see, because those failures only manifest in Community/SaaS Program.cs.
#
# Assumes the standard sibling-checkout layout: orkyo-foundation,
# orkyo-community, and orkyo-saas all live next to each other under one
# parent directory.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Verify sibling repos exist before doing any work
for repo in orkyo-foundation orkyo-community orkyo-saas; do
    if [[ ! -d "$ROOT/$repo/backend" ]]; then
        echo "❌ Missing sibling repo: $ROOT/$repo/backend"
        echo "   This script assumes orkyo-foundation, orkyo-community, and"
        echo "   orkyo-saas are checked out side-by-side under $ROOT."
        exit 2
    fi
done

declare -A solution_files=(
    [orkyo-foundation]="Orkyo.Foundation.slnx"
    [orkyo-community]="Orkyo.Community.slnx"
    [orkyo-saas]="Orkyo.Saas.slnx"
)

failed=()
for repo in orkyo-foundation orkyo-community orkyo-saas; do
    echo ""
    echo "=== $repo ==="
    slnx="${solution_files[$repo]}"
    if ! (cd "$ROOT/$repo" && dotnet test "$slnx" \
            --logger "console;verbosity=minimal"); then
        failed+=("$repo")
    fi
done

echo ""
if (( ${#failed[@]} > 0 )); then
    echo "❌ Failed: ${failed[*]}"
    exit 1
fi
echo "✅ All three product tests passed"
