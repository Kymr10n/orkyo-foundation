import { describe, it, expect, vi, afterEach } from 'vitest';
import { randomId } from './ids';

describe('randomId', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('uses crypto.randomUUID when available', () => {
    const spy = vi.spyOn(globalThis.crypto, 'randomUUID').mockReturnValue(
      '11111111-1111-1111-1111-111111111111'
    );
    expect(randomId()).toBe('11111111-1111-1111-1111-111111111111');
    spy.mockRestore();
  });

  it('falls back to a timestamp-based id when crypto is unavailable (non-secure context)', () => {
    vi.stubGlobal('crypto', undefined);
    const id = randomId();
    expect(id).toMatch(/^[0-9a-z]+$/);
    vi.unstubAllGlobals();
  });

  it('falls back when randomUUID itself is missing', () => {
    // Simulates an older secure-context engine without randomUUID.
    vi.stubGlobal('crypto', {});
    const id = randomId();
    expect(id).toMatch(/^[0-9a-z]+$/);
    vi.unstubAllGlobals();
  });

  it('produces non-colliding ids across calls', () => {
    const ids = new Set(Array.from({ length: 20 }, () => randomId()));
    expect(ids.size).toBe(20);
  });
});
