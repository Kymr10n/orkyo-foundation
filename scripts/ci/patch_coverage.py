#!/usr/bin/env python3
"""Report patch coverage: the lines this branch adds, intersected with cobertura reports.

Reads the same cobertura reports the test runs already emit, so it measures what
codecov measures without waiting for a PR. See scripts/ci/patch-coverage.sh.
"""
import argparse
import fnmatch
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@")


def ignored_globs(root):
    """The `ignore:` globs from codecov.yml, so the local number matches the PR comment.

    Parsed by hand rather than with PyYAML: the block is a flat list of quoted
    strings, and this script must run with a bare python3.
    """
    path = os.path.join(root, "codecov.yml")
    if not os.path.exists(path):
        return []
    globs, inside = [], False
    with open(path) as fh:
        for raw in fh:
            line = raw.rstrip("\n")
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            if line.startswith("ignore:"):
                inside = True
                continue
            if inside:
                stripped = line.strip()
                if not line.startswith((" ", "\t", "-")):
                    break  # next top-level key ends the block
                if stripped.startswith("- "):
                    globs.append(stripped[2:].strip().strip('"\''))
    return globs


def is_ignored(path, globs):
    # Codecov treats "**" as spanning path separators; fnmatch's "*" already does.
    return any(fnmatch.fnmatch(path, g.replace("**/", "*").replace("**", "*")) for g in globs)


def repo_root():
    return subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        capture_output=True, text=True, check=True).stdout.strip()


def added_lines(base, root):
    """Map repo-relative path -> set of line numbers this branch adds or changes."""
    merge_base = subprocess.run(
        ["git", "merge-base", base, "HEAD"],
        cwd=root, capture_output=True, text=True, check=True).stdout.strip()
    diff = subprocess.run(
        ["git", "diff", "--unified=0", "--diff-filter=d", merge_base, "--", "."],
        cwd=root, capture_output=True, text=True, check=True).stdout

    result, path, line = {}, None, 0
    for raw in diff.splitlines():
        if raw.startswith("+++ b/"):
            path = raw[6:]
        elif raw.startswith("@@"):
            m = HUNK.match(raw)
            if m:
                line = int(m.group(1))
        elif raw.startswith("+") and not raw.startswith("+++") and path:
            if raw[1:].strip():
                result.setdefault(path, set()).add(line)
            line += 1
    return result


def coverage_by_file(reports, root):
    """Map repo-relative path -> {line number: hit count} across every report."""
    covered = {}
    for report in reports:
        if not os.path.exists(report):
            continue
        tree = ET.parse(report)
        bases = [s.text for s in tree.iter("source") if s.text] or [""]
        for cls in tree.iter("class"):
            filename = cls.get("filename") or ""
            path = None
            for base in bases:
                candidate = os.path.normpath(os.path.join(base, filename))
                rel = os.path.relpath(candidate, root)
                if not rel.startswith("..") and os.path.exists(candidate):
                    path = rel.replace(os.sep, "/")
                    break
            if path is None:
                continue
            lines = covered.setdefault(path, {})
            for ln in cls.iter("line"):
                number = int(ln.get("number", 0))
                hits = int(ln.get("hits", 0))
                taken, total = branch_counts(ln)
                before = lines.get(number, (0, 0, 0))
                lines[number] = (
                    before[0] + hits,
                    max(before[1], taken),
                    max(before[2], total),
                )
    return covered


def branch_counts(line):
    """(branches taken, branches total) for one cobertura <line>, or (0, 0) if not a branch.

    Codecov counts a line whose branches are only half taken as a PARTIAL, not a hit —
    an `if (x) throw` one-liner where only one side ever runs. Reading it here is what
    makes this script's total match the number codecov reports.
    """
    if line.get("branch") != "true":
        return 0, 0
    match = re.search(r"\((\d+)/(\d+)\)", line.get("condition-coverage") or "")
    if match:
        return int(match.group(1)), int(match.group(2))
    return 0, 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="origin/main", help="branch to diff against")
    ap.add_argument("--report", action="append", default=[], help="cobertura xml (repeatable)")
    ap.add_argument("--target", type=float, default=80.0, help="patch coverage percent required")
    args = ap.parse_args()

    root = repo_root()
    added = added_lines(args.base, root)
    covered = coverage_by_file(args.report, root)
    globs = ignored_globs(root)

    total_hit = total_measured = 0
    rows = []
    for path in sorted(added):
        if is_ignored(path, globs):
            continue
        lines = covered.get(path)
        if not lines:
            continue  # not a measured source file: test, config, docs, or codecov-ignored
        measurable = sorted(added[path] & lines.keys())
        if not measurable:
            continue
        # Codecov's arithmetic: a line counts fully only when it ran AND every branch on
        # it was taken. A half-taken branch is a partial and scores nothing.
        missing = [n for n in measurable if lines[n][0] == 0]
        partial = [n for n in measurable
                   if lines[n][0] > 0 and lines[n][2] > 0 and lines[n][1] < lines[n][2]]
        hit = len(measurable) - len(missing) - len(partial)
        total_hit += hit
        total_measured += len(measurable)
        rows.append((path, hit, len(measurable), missing, partial))

    if not total_measured:
        print("patch coverage: no measured source lines changed against %s" % args.base)
        return 0

    print("Patch coverage against %s\n" % args.base)
    for path, hit, measured, missing, partial in sorted(rows, key=lambda r: r[1] / r[2]):
        pct = 100.0 * hit / measured
        flag = "  " if pct >= args.target else "!!"
        print("%s %6.2f%%  %3d/%-3d  %s" % (flag, pct, hit, measured, path))
        if missing:
            print("      uncovered: %s" % ", ".join(str(n) for n in missing))
        if partial:
            print("      partial (one branch only): %s"
                  % ", ".join(str(n) for n in partial))

    pct = 100.0 * total_hit / total_measured
    print("\nTOTAL %.2f%% (%d/%d lines) — target %.0f%%" % (pct, total_hit, total_measured, args.target))
    if pct < args.target:
        print("\nPatch coverage is below target. Cover rejection and error branches first.")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
