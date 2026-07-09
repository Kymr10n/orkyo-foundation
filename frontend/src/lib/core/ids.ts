/**
 * `crypto.randomUUID` is only exposed in secure contexts (HTTPS or localhost).
 * Community self-hosts may be reached over plain HTTP on a LAN, so calling it
 * directly throws there. Use this helper instead of `crypto.randomUUID()` at
 * any client-side call site.
 */
export function randomId(): string {
  return globalThis.crypto?.randomUUID?.() ?? Date.now().toString(36);
}
