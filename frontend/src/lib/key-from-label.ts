/**
 * Derives a storage key from a human label: "Serial number" → "serial_number".
 *
 * The key is what the value is stored under and is immutable once anything has been written, so
 * it is suggested from the label rather than asked for — one fewer thing to get wrong, and the
 * author can still override it before the first save.
 *
 * The shape matches the server's CHECK constraint (`^[a-z][a-z0-9_]{0,49}$`) on both
 * `resource_custom_fields.key` and `list_columns.key`: lowercase, alphanumeric and underscores,
 * starting with a letter, at most 50 characters. Shared by custom fields and list columns
 * precisely so those two cannot drift apart from each other or from the constraint.
 */
export function keyFromLabel(label: string): string {
  return label
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '')
    // A key must start with a letter; prefixing beats dropping a leading digit, which would
    // silently turn "3d printer" into "d_printer".
    .replace(/^([^a-z])/, 'f_$1')
    .slice(0, 50);
}
