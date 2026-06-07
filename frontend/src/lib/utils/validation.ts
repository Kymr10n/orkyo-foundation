/**
 * Returns true when `email` matches a valid email pattern.
 * Requires at least one non-whitespace/non-@ char before the @, a domain
 * segment, a literal dot, and a TLD of at least one character.
 * An empty string returns false — callers with optional fields must guard
 * with `!email || isValidEmail(email)`.
 */
export function isValidEmail(email: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
}
