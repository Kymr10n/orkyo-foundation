/**
 * Normalize an unknown rejection into a human string for toast descriptions. A non-`Error`
 * rejection reads as `fallback` when one is given, else as `String(err)`.
 */
export function errorMessage(err: unknown, fallback?: string): string {
  return err instanceof Error ? err.message : (fallback ?? String(err));
}

/** Variables of a create-or-update save: `id: null` creates, an id updates that entity. */
export type SaveVariables<TCreate, TUpdate = TCreate> =
  | { id: null; data: TCreate }
  | { id: string; data: TUpdate };

/** `meta.successMessage` for a {@link SaveVariables} mutation: one text per branch. */
export const savedMessage =
  (created: string, updated: string) =>
  (_data: unknown, variables: unknown): string =>
    (variables as { id: string | null }).id === null ? created : updated;
