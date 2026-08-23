import { describe, it, expect, vi, beforeEach } from 'vitest';

// API_BASE_URL is read inside buildUrl on every call, so a getter lets one suite cover
// both deployment shapes: split-origin dev and same-origin production.
const config = vi.hoisted(() => ({ base: '' }));

vi.mock('../core/api-utils', () => ({
  get API_BASE_URL() {
    return config.base;
  },
  getApiHeaders: vi.fn(() => ({
    'Content-Type': 'application/json',
    'X-Tenant-Slug': 'acme',
  })),
  handleApiError: vi.fn(),
}));

import { streamAiChat } from './ai-api';

/** Drains the generator; the turn ends immediately because the response is not ok. */
async function runTurn() {
  const events = [];
  for await (const event of streamAiChat({ transcript: [] })) events.push(event);
  return events;
}

describe('streamAiChat request shape', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => ({ ok: false, status: 404, body: null })),
    );
  });

  it('sends the turn to the configured API origin, not the page origin', async () => {
    // The regression: a bare relative path resolves against the page, so in split-origin
    // dev the turn reached the frontend server and 404'd while every other AI call worked.
    config.base = 'http://api.example.test';

    await runTurn();

    const [url] = vi.mocked(fetch).mock.calls[0];
    expect(url).toBe('http://api.example.test/api/ai/chat');
  });

  it('falls back to same-origin when no API base is configured', async () => {
    config.base = '';

    await runTurn();

    const [url] = vi.mocked(fetch).mock.calls[0];
    expect(url).toBe(`${window.location.origin}/api/ai/chat`);
  });

  it('carries the shared headers, so the backend can resolve the workspace', async () => {
    config.base = '';

    await runTurn();

    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(init?.headers).toMatchObject({ 'X-Tenant-Slug': 'acme' });
    expect(init?.credentials).toBe('include');
  });

  it('reports an unreachable assistant rather than throwing', async () => {
    config.base = '';

    const events = await runTurn();

    expect(events).toEqual([
      { type: 'error', code: 'request_failed', message: 'The assistant could not be reached (404).' },
    ]);
  });
});
