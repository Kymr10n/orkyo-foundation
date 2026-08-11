import { describe, it, expect } from 'vitest';
import { stableStringify } from './stable-stringify';

describe('stableStringify', () => {
  it('reads two objects with the same data as equal whatever the key order', () => {
    expect(stableStringify({ b: 2, a: 1 })).toBe(stableStringify({ a: 1, b: 2 }));
  });

  it('still separates objects whose data differs', () => {
    expect(stableStringify({ a: 1 })).not.toBe(stableStringify({ a: 2 }));
    expect(stableStringify({ a: 1 })).not.toBe(stableStringify({ a: 1, b: 1 }));
  });

  it('sorts nested objects too — a form holds its map one level down', () => {
    const a = { name: 'Lathe', customFields: { serial: 'SN-1', notes: 'hot' } };
    const b = { name: 'Lathe', customFields: { notes: 'hot', serial: 'SN-1' } };
    expect(stableStringify(a)).toBe(stableStringify(b));
  });

  it('leaves array order alone — there, order is the data', () => {
    expect(stableStringify([1, 2])).not.toBe(stableStringify([2, 1]));
  });

  it('handles the values a form actually holds', () => {
    expect(stableStringify({ a: null, b: false, c: 0, d: '' })).toBe(
      '{"a":null,"b":false,"c":0,"d":""}',
    );
  });
});
