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
 * Only a flag is stored; the destination is computed when it is read. A URL in storage would
 * be a redirect anyone able to write this origin's sessionStorage could steer.
 *
 * The flag is NOT consumed on read. A session usually ends in a burst of concurrent 401s
 * (polls, the page's own queries), and each handler must reach the same answer — a single-use
 * marker let the first 401 head for the marketing site and the next one override it with the
 * login flow. The next successful bootstrap sets or clears the flag, which is the one place the
 * tab learns what kind of session it holds, so a later real login is unaffected.
 *
 * Deliberately generic: foundation knows "this session came through a secondary client and is
 * therefore ephemeral", not "this is the SaaS demo". Community never sets it.
 */
const SESSION_END_REDIRECT_KEY = "orkyo:session-end-redirect";

/**
 * Records whether this session is ephemeral, based on the bootstrap response's `authClient`.
 * Call on every successful bootstrap: a null/absent value clears any stale flag left by a
 * previous demo session in the same tab.
 */
export function rememberSessionEndRedirect(authClient: string | null | undefined): void {
  try {
    if (authClient) {
      sessionStorage.setItem(SESSION_END_REDIRECT_KEY, "1");
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
 * public demo, today. Safe to ask during render.
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
 * The URL to send the visitor to when this session ends, or null when it ends the ordinary way.
 */
export function sessionEndRedirect(): string | null {
  return isEphemeralSession() ? `${getApexOrigin()}/` : null;
}
