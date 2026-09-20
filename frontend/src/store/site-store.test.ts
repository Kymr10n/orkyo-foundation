import { describe, it, expect, beforeEach } from 'vitest';
import { STORAGE_KEYS } from '@foundation/src/constants/storage';
import { useSiteStore } from '@foundation/src/store/site-store';

function persisted(): { selectedSiteId?: string | null } {
  const raw = localStorage.getItem(STORAGE_KEYS.SELECTED_SITE_ID);
  return raw ? (JSON.parse(raw) as { state: { selectedSiteId?: string | null } }).state : {};
}

describe('useSiteStore', () => {
  beforeEach(() => {
    localStorage.clear();
    useSiteStore.setState({ selectedSiteId: null });
  });

  it('sets the selected site', () => {
    useSiteStore.getState().setSelectedSiteId('site-1');
    expect(useSiteStore.getState().selectedSiteId).toBe('site-1');
  });

  it('persists the selected site', () => {
    useSiteStore.getState().setSelectedSiteId('site-1');
    expect(persisted().selectedSiteId).toBe('site-1');
  });

  it('persists "all sites" too, so a reload keeps the choice', () => {
    useSiteStore.getState().setSelectedSiteId('site-1');
    useSiteStore.getState().setSelectedSiteId(null);
    expect(useSiteStore.getState().selectedSiteId).toBeNull();
    expect(persisted().selectedSiteId).toBeNull();
  });
});
