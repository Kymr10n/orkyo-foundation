import { describe, it, expect, beforeEach } from 'vitest';
import { tenantStorage } from './tenant-storage';

beforeEach(() => localStorage.clear());

describe('tenantStorage', () => {
  it('saves, reads and clears the slug', () => {
    tenantStorage.save('acme');
    expect(tenantStorage.slug()).toBe('acme');
    tenantStorage.clear();
    expect(tenantStorage.slug()).toBe('');
  });

  it('clear() also removes the legacy active_membership entry', () => {
    localStorage.setItem('active_membership', JSON.stringify({ slug: 'acme', breakGlassSessionId: 'bg-1' }));
    tenantStorage.clear();
    expect(localStorage.getItem('active_membership')).toBeNull();
  });
});
