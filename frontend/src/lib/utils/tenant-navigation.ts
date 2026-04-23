/**
 * Tenant subdomain navigation utilities.
 *
 * When baseDomain is configured (e.g. "orkyo.com"), tenant selection should
 * redirect the browser to {slug}.{baseDomain} instead of staying on the
 * current origin. This is required because the backend resolves tenants
 * from the subdomain — the X-Tenant-Slug header is ignored in production.
 *
 * ALL hostname logic that involves subdomainPrefix lives here.
 * Other modules should call these helpers — never read subdomainPrefix directly.
 */

import { runtimeConfig } from "@foundation/src/config/runtime";

// ── Hostname helpers (pure, no side-effects) ─────────────────────────────────

/**
 * Build the full hostname for a tenant subdomain.
 *
 * Production: `{slug}.orkyo.com`
 * Staging:    `staging-{slug}.orkyo.com`  (subdomainPrefix = "staging-")
 *
 * Returns null when baseDomain is not configured (local dev).
 */
export function getTenantHostname(slug: string): string | null {
  const { baseDomain, subdomainPrefix } = runtimeConfig;
  if (!baseDomain) return null;
  return `${subdomainPrefix || ""}${slug}.${baseDomain}`;
}

/**
 * The "apex" hostname for the current environment.
 *
 * Production: baseDomain (e.g. "orkyo.com")
 * Staging:    subdomainPrefix without trailing dash + baseDomain
 *             (e.g. prefix "staging-" → "staging.orkyo.com")
 *
 * Returns null when baseDomain is not configured.
 */
export function getApexHostname(): string | null {
  const { baseDomain, subdomainPrefix } = runtimeConfig;
  if (!baseDomain) return null;
  if (subdomainPrefix) {
    const envLabel = subdomainPrefix.replace(/-$/, '');
    return envLabel ? `${envLabel}.${baseDomain}` : baseDomain;
  }
  return baseDomain;
}

/**
 * Full origin URL for the apex (e.g. "https://orkyo.com" or "https://staging.orkyo.com").
 * Falls back to the current origin when baseDomain is not configured.
 */
export function getApexOrigin(): string {
  const apex = getApexHostname();
  return apex
    ? `${window.location.protocol}//${apex}`
    : window.location.origin;
}

/**
 * Extract the tenant slug from a hostname, stripping the environment prefix.
 *
 * Returns null when:
 * - baseDomain is not configured
 * - hostname is the apex (no subdomain)
 * - hostname has nested subdomains (e.g. a.b.orkyo.com)
 * - prefix is configured but hostname doesn't match it
 */
export function extractSlugFromHostname(hostname: string): string | null {
  const { baseDomain, subdomainPrefix } = runtimeConfig;
  if (!baseDomain) return null;

  if (hostname === baseDomain || !hostname.endsWith(`.${baseDomain}`)) {
    return null;
  }

  const subdomain = hostname.slice(0, -(baseDomain.length + 1));
  if (!subdomain || subdomain.includes(".")) return null;

  if (subdomainPrefix) {
    return subdomain.startsWith(subdomainPrefix)
      ? subdomain.slice(subdomainPrefix.length) || null
      : null;
  }
  return subdomain;
}

/**
 * Extract the tenant slug from the current hostname, or null if on the apex.
 * Convenience wrapper around `extractSlugFromHostname(window.location.hostname)`.
 */
export function getCurrentSubdomain(): string | null {
  return extractSlugFromHostname(window.location.hostname);
}

// ── Navigation (side-effects — redirect the browser) ─────────────────────────

/**
 * Navigate to a tenant's subdomain. Does a full-page redirect.
 *
 * Returns true if a redirect was initiated, false if subdomain routing is
 * not configured or the browser is already on the correct subdomain.
 * Callers should `return` after a true result — the current page will unload.
 */
export function navigateToTenantSubdomain(slug: string, path = "/"): boolean {
  const targetHost = getTenantHostname(slug);
  if (!targetHost) return false;
  if (window.location.hostname === targetHost) return false;

  window.location.href = `${window.location.protocol}//${targetHost}${path}`;
  return true;
}

/**
 * Redirect to the apex domain's login page.
 * Used on tenant subdomains when session is invalid (401, unauthenticated).
 * Returns false if baseDomain is not configured or already on apex.
 */
function navigateToApexLogin(): boolean {
  return navigateToApex("/login");
}

/**
 * Redirect to login — works in all environments.
 *
 * Production (tenant subdomain): full redirect to apex `/login`.
 * Local dev (no baseDomain): full page reload to `/` which re-enters
 * the auth pipeline (ApexGateway re-evaluates authStage).
 *
 * This is the single function all callers should use instead of the
 * `if (!navigateToApexLogin()) { window.location.href = '...' }` pattern.
 */
export function redirectToLogin(): void {
  if (!navigateToApexLogin()) {
    window.location.href = "/";
  }
}

/**
 * Navigate to the apex domain (e.g. orkyo.com, or staging.orkyo.com on staging).
 * Used when exiting a tenant subdomain (e.g. break-glass exit → back to /admin).
 */
export function navigateToApex(path = "/"): boolean {
  const apex = getApexHostname();
  if (!apex) return false;
  if (window.location.hostname === apex) return false;

  window.location.href = `${window.location.protocol}//${apex}${path}`;
  return true;
}

// ── Break-glass cookie ───────────────────────────────────────────────────────
// When a site admin enters a tenant via the admin panel, a short-lived cookie
// is set on the shared base domain (e.g. .orkyo.com) before redirecting to
// the tenant subdomain. The auth callback on the new subdomain reads it to
// construct a break-glass membership.

const BREAK_GLASS_COOKIE = "orkyo-break-glass";

/**
 * Set a cross-subdomain cookie carrying the break-glass session ID and tenant UUID.
 * Expires in 60 seconds — just enough for the redirect + OIDC re-auth.
 */
export function setBreakGlassCookie(sessionId: string, tenantId: string): void {
  const { baseDomain } = runtimeConfig;
  if (!baseDomain) return;

  const expires = new Date(Date.now() + 60_000).toUTCString();
  document.cookie = `${BREAK_GLASS_COOKIE}=${sessionId}|${tenantId}; domain=.${baseDomain}; path=/; expires=${expires}; secure; samesite=lax`;
}

/**
 * Read and clear the break-glass cookie.
 * Returns `{ sessionId, tenantId }` or null if absent.
 */
export function consumeBreakGlassCookie(): { sessionId: string; tenantId: string } | null {
  const match = new RegExp(`${BREAK_GLASS_COOKIE}=([^;]+)`).exec(document.cookie);
  if (!match) return null;

  // Clear the cookie immediately
  const { baseDomain } = runtimeConfig;
  if (baseDomain) {
    document.cookie = `${BREAK_GLASS_COOKIE}=; domain=.${baseDomain}; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT; secure; samesite=lax`;
  }

  const [sessionId, tenantId = ''] = match[1].split('|');
  return { sessionId, tenantId };
}
