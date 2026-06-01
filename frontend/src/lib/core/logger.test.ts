import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// ── Module-level mock so we can flip isDev per test ───────────────────────────
const mockRuntimeConfig = { isDev: false };
vi.mock('@foundation/src/config/runtime', () => ({
  runtimeConfig: mockRuntimeConfig,
}));

// Import AFTER mock so the module captures our mock value.
// Because isDev is read at module-eval time we must re-import between tests;
// use vi.resetModules() + dynamic import.

describe('logger', () => {
  beforeEach(() => {
    vi.spyOn(console, 'log').mockImplementation(() => {});
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    vi.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.resetModules();
  });

  // ── Production mode (isDev = false) ────────────────────────────────────────

  describe('production mode (isDev = false)', () => {
    async function getLogger() {
      mockRuntimeConfig.isDev = false;
      const mod = await import('./logger');
      return mod.logger;
    }

    it('debug does not call console.log', async () => {
      const logger = await getLogger();
      logger.debug('should be silent');
      expect(console.log).not.toHaveBeenCalled();
    });

    it('info does not call console.log', async () => {
      const logger = await getLogger();
      logger.info('should be silent');
      expect(console.log).not.toHaveBeenCalled();
    });

    it('warn calls console.warn', async () => {
      const logger = await getLogger();
      logger.warn('something risky');
      expect(console.warn).toHaveBeenCalledWith('[WARN]', 'something risky');
    });

    it('error calls console.error', async () => {
      const logger = await getLogger();
      logger.error('something broke');
      expect(console.error).toHaveBeenCalledWith('[ERROR]', 'something broke');
    });
  });

  // ── Development mode (isDev = true) ────────────────────────────────────────

  describe('development mode (isDev = true)', () => {
    async function getLogger() {
      mockRuntimeConfig.isDev = true;
      const mod = await import('./logger');
      return mod.logger;
    }

    it('debug calls console.log with [DEBUG] prefix', async () => {
      const logger = await getLogger();
      logger.debug('hello', { extra: 1 });
      expect(console.log).toHaveBeenCalledWith('[DEBUG]', 'hello', { extra: 1 });
    });

    it('info calls console.log with [INFO] prefix', async () => {
      const logger = await getLogger();
      logger.info('loaded');
      expect(console.log).toHaveBeenCalledWith('[INFO]', 'loaded');
    });

    it('warn still calls console.warn in dev mode', async () => {
      const logger = await getLogger();
      logger.warn('heads up');
      expect(console.warn).toHaveBeenCalledWith('[WARN]', 'heads up');
    });

    it('error still calls console.error in dev mode', async () => {
      const logger = await getLogger();
      logger.error(new Error('boom'));
      expect(console.error).toHaveBeenCalledWith('[ERROR]', expect.any(Error));
    });
  });
});
