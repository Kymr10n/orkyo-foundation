import { getApexOrigin } from "./tenant-navigation";

/**
 * Where to send the visitor when an *ephemeral* session ends.
 *
 * Ordinary sessions end at the login flow, which is right: the user has credentials and
 * usually re-authenticates silently against the still-live Keycloak SSO cookie. A session
 * established through a secondary OAuth client — today the public demo — is different: that
 * visitor never had credentials, so bouncing them to a password form at the end of the demo
 * is a dead end. They belong back on the marketing site they came from.
 *
 * Kept in sessionStorage rather than React state because the decision has to survive the
 * hard navigation that ends the session, and has to be readable from `api-utils`, which sits
 * below the component tree and cannot reach a context.
 *
 * Deliberately generic: foundation knows "this session came through a secondary client and is
 * therefore ephemeral", not "this is the SaaS demo". Community never sets it.
 */
const SESSION_END_REDIRECT_KEY = "orkyo:session-end-redirect";

/**
 * Records where this session should end, based on the bootstrap response's `authClient`.
 * Call on every successful bootstrap: a null/absent value clears any stale marker left by a
 * previous demo session in the same tab.
 */
export function rememberSessionEndRedirect(authClient: string | null | undefined): void {
  try {
    if (authClient) {
      sessionStorage.setItem(SESSION_END_REDIRECT_KEY, `${getApexOrigin()}/`);
    } else {
      sessionStorage.removeItem(SESSION_END_REDIRECT_KEY);
    }
  } catch {
    // Private-mode/quota failures must never break auth. Falling back to the normal login
    // redirect is a worse demo ending, not a broken app.
  }
}

/**
 * Whether this session came through a secondary client and is therefore ephemeral — the
 * public demo, today. Peeks without consuming, so it is safe to ask during render; use
 * {@link takeSessionEndRedirect} when actually ending the session.
 *
 * Lets a surface offer an ephemeral visitor something an account holder would not want, such
 * as a way to ask for a guided demonstration when a demo limit is reached.
 */
export function isEphemeralSession(): boolean {
  try {
    return sessionStorage.getItem(SESSION_END_REDIRECT_KEY) !== null;
  } catch {
    return false;
  }
}

/**
 * Consumes the marker. Returns the URL to send the visitor to, or null when this session ends
 * the ordinary way. Single-use: reading it clears it, so a later real login is unaffected.
 */
export function takeSessionEndRedirect(): string | null {
  try {
    const target = sessionStorage.getItem(SESSION_END_REDIRECT_KEY);
    if (target) sessionStorage.removeItem(SESSION_END_REDIRECT_KEY);
    return target;
  } catch {
    return null;
  }
}
