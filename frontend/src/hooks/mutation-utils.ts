/**
 * Normalize an unknown rejection into a human string for toast descriptions. A non-`Error`
 * rejection reads as `fallback` when one is given, else as `String(err)`.
 */
export function errorMessage(err: unknown, fallback?: string): string {
  return err instanceof Error ? err.message : (fallback ?? String(err));
}
