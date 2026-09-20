import { describe, it, expect } from 'vitest';
import { hasCustomFieldValue } from './custom-field-value';

describe('hasCustomFieldValue', () => {
  it('treats null, undefined and whitespace as unfilled', () => {
    expect(hasCustomFieldValue(null)).toBe(false);
    expect(hasCustomFieldValue(undefined)).toBe(false);
    expect(hasCustomFieldValue('   ')).toBe(false);
  });

  it('treats zero and false as filled in — they are answers', () => {
    expect(hasCustomFieldValue(0)).toBe(true);
    expect(hasCustomFieldValue(false)).toBe(true);
  });
});
