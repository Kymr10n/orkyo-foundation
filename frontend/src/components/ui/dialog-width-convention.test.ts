import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Dialog widths come from DIALOG_SIZE, not from literals at the call site.
 *
 * The app had drifted to eighteen hand-written widths — 425, 448, 500, 512, 540, 550, 640 and
 * 672px — which is why dialogs visibly disagreed with each other. A grep-level guard is enough
 * to hold the line: a token reaches the element as `{DIALOG_SIZE.lg}` or `cn(DIALOG_SIZE.lg, …)`,
 * never as a `max-w-*` string, so any string literal here is by definition a new hardcoded width.
 */

/** A command palette is a search surface, not a form; its width follows the cmdk convention. */
const ALLOWED = ['components/layout/CommandPalette.tsx'];

const SRC = join(__dirname, '..', '..');

function tsxFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) return tsxFiles(full);
    return entry.endsWith('.tsx') && !entry.includes('.test.') ? [full] : [];
  });
}

describe('dialog width convention', () => {
  it('has no hardcoded max-w-* on a DialogContent outside the allowlist', () => {
    // Matches a className string literal on the opening tag, across line breaks. Deliberately
    // not /g: `.test()` on a global regex carries `lastIndex` between files and starts the next
    // search mid-string, so a later offender can slip through.
    const pattern = /<(?:Alert)?DialogContent[^>]*className=\s*["'`][^"'`]*\bmax-w-/;

    const offenders = tsxFiles(SRC)
      .filter((file) => pattern.test(readFileSync(file, 'utf8')))
      .map((file) => file.slice(SRC.length + 1).replaceAll('\\', '/'))
      .filter((rel) => !ALLOWED.includes(rel))
      .sort();

    expect(offenders).toEqual([]);
  });
});
