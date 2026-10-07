import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

const { mockRuntimeConfig } = vi.hoisted(() => ({ mockRuntimeConfig: { isDev: false } }));
vi.mock('@foundation/src/config/runtime', () => ({ runtimeConfig: mockRuntimeConfig }));
vi.mock('./api-client', () => ({ buildUrl: (p: string) => `https://app.test${p}` }));

import {
  installGlobalErrorHandlers,
  reportClientError,
  reportWebVital,
  resetClientErrorReporting,
  VITAL_SAMPLE_RATE,
} from './client-errors';

/** The JSON bodies handed to sendBeacon so far. */
async function sentBodies(beacon: ReturnType<typeof vi.fn>): Promise<Record<string, unknown>[]> {
  return Promise.all(
    beacon.mock.calls.map(async ([, blob]) => JSON.parse(await (blob as Blob).text()) as Record<string, unknown>),
  );
}

describe('client-errors', () => {
  let beacon: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    mockRuntimeConfig.isDev = false;
    resetClientErrorReporting();
    beacon = vi.fn(() => true);
    vi.stubGlobal('navigator', { sendBeacon: beacon });
    vi.spyOn(console, 'debug').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('sends an error as one beacon with message, stack, route and kind', async () => {
    const sent = reportClientError('error', new Error('kaboom'));

    expect(sent).toBe(true);
    expect(beacon).toHaveBeenCalledTimes(1);
    expect(beacon.mock.calls[0][0]).toBe('https://app.test/api/client-errors');
    const [body] = await sentBodies(beacon);
    expect(body).toMatchObject({ kind: 'error', message: 'kaboom', route: window.location.pathname });
    expect(body.stack).toContain('kaboom');
  });

  it('describes a non-Error rejection reason as its JSON', async () => {
    reportClientError('unhandledrejection', { code: 42 });
    const [body] = await sentBodies(beacon);
    expect(body.message).toBe('{"code":42}');
    expect(body.stack).toBeUndefined();
  });

  it('sends the same message once per minute', () => {
    expect(reportClientError('error', new Error('again'))).toBe(true);
    expect(reportClientError('error', new Error('again'))).toBe(false);
    expect(reportClientError('render', new Error('again'))).toBe(true);
    expect(beacon).toHaveBeenCalledTimes(2);
  });

  it('stops after ten reports in one page load', () => {
    for (let i = 0; i < 10; i += 1) expect(reportClientError('error', new Error(`e${i}`))).toBe(true);
    expect(reportClientError('error', new Error('one too many'))).toBe(false);
    expect(beacon).toHaveBeenCalledTimes(10);
  });

  it('skips a stale-chunk error, which the reload handles', () => {
    const stale = new TypeError('Failed to fetch dynamically imported module: /app/assets/x.js');
    expect(reportClientError('error', stale)).toBe(false);
    expect(beacon).not.toHaveBeenCalled();
  });

  it('truncates a very long message and stack to the server limits', async () => {
    const error = new Error('m'.repeat(5000));
    error.stack = 's'.repeat(20000);
    reportClientError('error', error);
    const [body] = await sentBodies(beacon);
    expect((body.message as string).length).toBe(2000);
    expect((body.stack as string).length).toBe(8000);
  });

  it('sends nothing in a development build and says so on the console', () => {
    mockRuntimeConfig.isDev = true;
    expect(reportClientError('error', new Error('dev'))).toBe(false);
    expect(beacon).not.toHaveBeenCalled();
    expect(console.debug).toHaveBeenCalled();
  });

  it('falls back to a keepalive fetch when sendBeacon is missing', () => {
    vi.stubGlobal('navigator', {});
    const fetchMock = vi.fn(() => Promise.resolve(new Response(null, { status: 202 })));
    vi.stubGlobal('fetch', fetchMock);

    expect(reportClientError('error', new Error('old engine'))).toBe(true);
    expect(fetchMock).toHaveBeenCalledWith(
      'https://app.test/api/client-errors',
      expect.objectContaining({ method: 'POST', keepalive: true }),
    );
  });

  describe('reportWebVital', () => {
    it('ships a sampled vital rounded to three decimals', async () => {
      expect(reportWebVital('CLS', 0.123456, 0)).toBe(true);
      const [body] = await sentBodies(beacon);
      expect(body).toMatchObject({ kind: 'vital', vitalName: 'CLS', vitalValue: 0.123 });
    });

    it('drops the sessions outside the sample', () => {
      expect(reportWebVital('LCP', 1200, VITAL_SAMPLE_RATE)).toBe(false);
      expect(beacon).not.toHaveBeenCalled();
    });
  });

  describe('installGlobalErrorHandlers', () => {
    it('reports window errors and unhandled rejections, installing once', async () => {
      const add = vi.spyOn(window, 'addEventListener');
      installGlobalErrorHandlers();
      installGlobalErrorHandlers();
      expect(add.mock.calls.filter(([type]) => type === 'error' || type === 'unhandledrejection')).toHaveLength(2);

      window.dispatchEvent(new ErrorEvent('error', { error: new Error('global'), message: 'global' }));
      const rejection = new Event('unhandledrejection') as PromiseRejectionEvent;
      Object.defineProperty(rejection, 'reason', { value: new Error('rejected') });
      window.dispatchEvent(rejection);

      const bodies = await sentBodies(beacon);
      expect(bodies.map((b) => [b.kind, b.message])).toEqual([
        ['error', 'global'],
        ['unhandledrejection', 'rejected'],
      ]);
    });
  });
});
