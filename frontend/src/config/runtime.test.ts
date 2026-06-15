import { describe, it, expect, afterEach, vi } from 'vitest';

/**
 * Tests for the runtime configuration module.
 *
 * These exercise optionalEnv logic and verify that the
 * exported runtimeConfig object reads from the correct sources.
 *
 * Because runtime.ts executes at import time, we use dynamic imports
 * after configuring window.__RUNTIME_CONFIG__ for each scenario.
 */

describe('runtime config', () => {
  const originalConfig = window.__RUNTIME_CONFIG__;

  afterEach(() => {
    // Restore original runtime config
    window.__RUNTIME_CONFIG__ = originalConfig;
    vi.resetModules();
  });

  describe('optionalEnv', () => {
    it('returns fallback when value is missing', async () => {
      window.__RUNTIME_CONFIG__ = {};
      vi.resetModules();

      const { runtimeConfig } = await import('./runtime');

      // VITE_DEFAULT_TENANT is not in __RUNTIME_CONFIG__ → fallback ''
      expect(runtimeConfig.defaultTenant).toBe('');
    });

    it('returns the configured value when present', async () => {
      window.__RUNTIME_CONFIG__ = {
        DEFAULT_TENANT: 'acme',
      };
      vi.resetModules();

      const { runtimeConfig } = await import('./runtime');

      expect(runtimeConfig.defaultTenant).toBe('acme');
    });
  });

  describe('apiBaseUrl (same-origin mode)', () => {
    it('returns empty string when runtime config has empty API_BASE_URL (same-origin)', async () => {
      window.__RUNTIME_CONFIG__ = {
        API_BASE_URL: '',
      };
      vi.resetModules();

      const { runtimeConfig } = await import('./runtime');

      // Empty string is a valid value meaning "same-origin" — must not fall
      // through to import.meta.env which may have a localhost dev URL baked in.
      expect(runtimeConfig.apiBaseUrl).toBe('');
    });

    it('returns the URL when API_BASE_URL is explicitly set', async () => {
      window.__RUNTIME_CONFIG__ = {
        API_BASE_URL: 'https://api.orkyo.com',
      };
      vi.resetModules();

      const { runtimeConfig } = await import('./runtime');

      expect(runtimeConfig.apiBaseUrl).toBe('https://api.orkyo.com');
    });

    it('apiBaseUrl is no longer a requireEnv (does not throw when missing)', async () => {
      window.__RUNTIME_CONFIG__ = {
        // API_BASE_URL intentionally omitted
      };
      vi.resetModules();

      const { runtimeConfig } = await import('./runtime');
      expect(typeof runtimeConfig.apiBaseUrl).toBe('string');
    });
  });

  describe('baseDomain', () => {
    it('defaults to empty string when not configured', async () => {
      window.__RUNTIME_CONFIG__ = {};
      vi.resetModules();

      const { runtimeConfig } = await import('./runtime');

      expect(runtimeConfig.baseDomain).toBe('');
    });

    it('returns the configured base domain', async () => {
      window.__RUNTIME_CONFIG__ = {
        BASE_DOMAIN: 'orkyo.com',
      };
      vi.resetModules();

      const { runtimeConfig } = await import('./runtime');

      expect(runtimeConfig.baseDomain).toBe('orkyo.com');
    });
  });

  describe('supportEmail', () => {
    it('derives the support email domain from baseDomain', async () => {
      window.__RUNTIME_CONFIG__ = {
        BASE_DOMAIN: 'orkyo.app',
      };
      vi.resetModules();

      const { runtimeConfig } = await import('./runtime');

      // Domain is never hardcoded — it follows the environment-provided apex.
      expect(runtimeConfig.supportEmail).toBe('support@orkyo.app');
    });

    it('follows baseDomain to a different apex', async () => {
      window.__RUNTIME_CONFIG__ = {
        BASE_DOMAIN: 'orkyo.com',
      };
      vi.resetModules();

      const { runtimeConfig } = await import('./runtime');

      expect(runtimeConfig.supportEmail).toBe('support@orkyo.com');
    });

    it('is empty when no base domain is configured (no hardcoded fallback)', async () => {
      window.__RUNTIME_CONFIG__ = {};
      vi.resetModules();

      const { runtimeConfig } = await import('./runtime');

      expect(runtimeConfig.supportEmail).toBe('');
    });

    it('can be overridden wholesale via SUPPORT_EMAIL', async () => {
      window.__RUNTIME_CONFIG__ = {
        BASE_DOMAIN: 'orkyo.app',
        SUPPORT_EMAIL: 'help@desk.example',
      };
      vi.resetModules();

      const { runtimeConfig } = await import('./runtime');

      expect(runtimeConfig.supportEmail).toBe('help@desk.example');
    });
  });

  describe('__RUNTIME_CONFIG__ precedence', () => {
    it('runtime config takes precedence over import.meta.env', async () => {
      window.__RUNTIME_CONFIG__ = {
        BASE_DOMAIN: 'runtime-value',
      };
      vi.resetModules();

      const { runtimeConfig } = await import('./runtime');

      expect(runtimeConfig.baseDomain).toBe('runtime-value');
    });
  });

  describe('OIDC derived values', () => {
    it('does not expose OIDC redirect URIs (BFF handles callbacks server-side)', async () => {
      window.__RUNTIME_CONFIG__ = {};
      vi.resetModules();

      const { runtimeConfig } = await import('./runtime');

      // BFF mode: no client-side OIDC config at all
      expect(runtimeConfig).not.toHaveProperty('oidcAuthority');
      expect(runtimeConfig).not.toHaveProperty('oidcClientId');
      expect(runtimeConfig).not.toHaveProperty('oidcRedirectUri');
      expect(runtimeConfig).not.toHaveProperty('oidcPostLogoutRedirectUri');
    });
  });
});
