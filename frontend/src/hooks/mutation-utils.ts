/** Normalize an unknown rejection into a human string for toast descriptions. */
export function errorMessage(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
