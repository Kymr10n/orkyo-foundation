/**
 * JSON, with object keys in a fixed order — for comparing two values by content.
 *
 * `JSON.stringify` preserves insertion order, which is fine for a form whose fields are
 * declared in source but wrong for one carrying a map the user edits: clearing a custom-field
 * box and typing the same value back removes the key and re-appends it, so a form identical to
 * the one that was loaded compares as changed and the dialog asks to discard nothing. Key
 * order is not data, so a dirty check must not read it as data.
 */
export function stableStringify(value: unknown): string {
  return JSON.stringify(value, (_key, val) => {
    if (val === null || typeof val !== 'object' || Array.isArray(val)) return val;
    return Object.fromEntries(
      Object.entries(val as Record<string, unknown>).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)),
    );
  });
}
