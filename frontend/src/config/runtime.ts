/**
 * Runtime configuration.
 *
 * Reads from window.__RUNTIME_CONFIG__ (injected at container startup)
 * with fallback to import.meta.env (for local dev with Vite).
 *
 * THIS IS THE ONLY FILE THAT MAY ACCESS import.meta.env OR window.__RUNTIME_CONFIG__.
 * All other code must import from this module.
 */

declare global {
  interface Window {
    __RUNTIME_CONFIG__?: Record<string, string>;
  }
}

/** Runtime config source — container-injected values take precedence over Vite env. */
const cfg = window.__RUNTIME_CONFIG__ ?? {};

function optionalEnv(name: string, fallback: string): string {
  const runtimeKey = name.replace(/^VITE_/, '');
  // Runtime config (container-injected) takes precedence — use `in` not truthiness,
  // because empty string is a valid value (e.g. API_BASE_URL="" means same-origin).
  if (runtimeKey in cfg) return cfg[runtimeKey];
  if (name in cfg) return cfg[name];
  return import.meta.env[name] ?? fallback;
}

/**
 * Runtime configuration for the Orkyo frontend.
 * Validated and typed access to environment variables.
 */
export const runtimeConfig = {
  /** Base URL for API requests. Empty string = same-origin (subdomain mode). */
  apiBaseUrl: optionalEnv('VITE_API_BASE_URL', ''),

  /** Default tenant slug for development */
  defaultTenant: optionalEnv('VITE_DEFAULT_TENANT', ''),

  /** Base domain for multi-tenant subdomain detection */
  baseDomain: optionalEnv('VITE_BASE_DOMAIN', ''),

  /** Subdomain prefix for staging (e.g. "staging-" → staging-acme.orkyo.com) */
  subdomainPrefix: optionalEnv('VITE_SUBDOMAIN_PREFIX', ''),

  /** Whether we're running in development mode */
  isDev: !cfg.API_BASE_URL && import.meta.env.DEV,
} as const;
