#!/usr/bin/env bash
# Measure patch coverage locally, before the PR — the same number codecov reports.
#
#   scripts/ci/patch-coverage.sh                  # run both suites, then report
#   scripts/ci/patch-coverage.sh --report-only    # reuse the last run's reports
#   scripts/ci/patch-coverage.sh --backend        # backend only (--frontend likewise)
#   scripts/ci/patch-coverage.sh --base origin/release/0.19
#
# Exits non-zero when patch coverage is under 80%. See CLAUDE.md "Test coverage".
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
BASE="origin/main"
RUN_BACKEND=1
RUN_FRONTEND=1
REPORT_ONLY=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --base) BASE="$2"; shift 2 ;;
        --backend) RUN_FRONTEND=0; shift ;;
        --frontend) RUN_BACKEND=0; shift ;;
        --report-only) REPORT_ONLY=1; shift ;;
        -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

BACKEND_RESULTS="$ROOT/backend/tests/TestResults"
FRONTEND_REPORT="$ROOT/frontend/coverage/cobertura-coverage.xml"

# A failing suite still writes its report, and the patch number is what this script is
# for — so warn and carry on rather than exiting. A red suite is reported by the run
# itself; note that vitest also fails on its own GLOBAL thresholds, which are a separate
# bar from patch coverage.
if [[ $REPORT_ONLY -eq 0 && $RUN_BACKEND -eq 1 ]]; then
    echo "==> backend tests with coverage"
    rm -rf "$BACKEND_RESULTS"
    dotnet test "$ROOT/backend/tests/Orkyo.Foundation.Tests.csproj" \
        --collect:"XPlat Code Coverage" --results-directory "$BACKEND_RESULTS" \
        || echo "!! backend suite exited non-zero — reporting on the coverage it wrote"
fi

if [[ $REPORT_ONLY -eq 0 && $RUN_FRONTEND -eq 1 ]]; then
    echo "==> frontend tests with coverage"
    (cd "$ROOT/frontend" && npx vitest run --coverage) \
        || echo "!! frontend suite exited non-zero — reporting on the coverage it wrote"
fi

REPORTS=()
if [[ $RUN_BACKEND -eq 1 ]]; then
    while IFS= read -r f; do REPORTS+=(--report "$f"); done \
        < <(find "$BACKEND_RESULTS" -name 'coverage.cobertura.xml' 2>/dev/null)
fi
if [[ $RUN_FRONTEND -eq 1 && -f "$FRONTEND_REPORT" ]]; then
    REPORTS+=(--report "$FRONTEND_REPORT")
fi

if [[ ${#REPORTS[@]} -eq 0 ]]; then
    echo "no cobertura reports found — run without --report-only first" >&2
    exit 2
fi

exec python3 "$ROOT/scripts/ci/patch_coverage.py" --base "$BASE" "${REPORTS[@]}"
