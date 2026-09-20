import { describe, it, expect, beforeEach, vi } from 'vitest';
import { STORAGE_KEYS } from '@foundation/src/constants/storage';
import { useLayoutStore } from '@foundation/src/store/layout-store';

function persisted(): Record<string, unknown> {
  const raw = localStorage.getItem(STORAGE_KEYS.LAYOUT);
  return raw ? (JSON.parse(raw) as { state: Record<string, unknown> }).state : {};
}

describe('useLayoutStore — collapse flags', () => {
  beforeEach(() => {
    localStorage.clear();
    useLayoutStore.setState({
      isFloorplanCollapsed: false,
      isSidebarCollapsed: false,
      collapsedGroupIds: [],
    });
  });

  it('toggles the floorplan and persists it', () => {
    useLayoutStore.getState().setIsFloorplanCollapsed(true);
    expect(useLayoutStore.getState().isFloorplanCollapsed).toBe(true);
    expect(persisted().isFloorplanCollapsed).toBe(true);

    useLayoutStore.getState().setIsFloorplanCollapsed(false);
    expect(persisted().isFloorplanCollapsed).toBe(false);
  });

  it('toggles the sidebar and persists it', () => {
    useLayoutStore.getState().setIsSidebarCollapsed(true);
    expect(useLayoutStore.getState().isSidebarCollapsed).toBe(true);
    expect(persisted().isSidebarCollapsed).toBe(true);
  });

  it('collapses and expands a group', () => {
    useLayoutStore.getState().toggleGroupCollapse('group-1');
    expect(useLayoutStore.getState().collapsedGroupIds).toContain('group-1');

    useLayoutStore.getState().toggleGroupCollapse('group-1');
    expect(useLayoutStore.getState().collapsedGroupIds).not.toContain('group-1');
  });

  it('keeps several groups collapsed at once', () => {
    useLayoutStore.getState().toggleGroupCollapse('group-1');
    useLayoutStore.getState().toggleGroupCollapse('group-2');
    expect(useLayoutStore.getState().collapsedGroupIds).toEqual(['group-1', 'group-2']);
  });
});

describe('useLayoutStore — theme', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('dark');
    useLayoutStore.setState({ theme: 'system', resolvedTheme: 'dark' });
  });

  it('sets an explicit dark theme', () => {
    useLayoutStore.getState().setTheme('dark');
    expect(useLayoutStore.getState().theme).toBe('dark');
    expect(useLayoutStore.getState().resolvedTheme).toBe('dark');
    expect(persisted().theme).toBe('dark');
    expect(document.documentElement.classList.contains('dark')).toBe(true);
  });

  it('sets an explicit light theme', () => {
    useLayoutStore.getState().setTheme('light');
    expect(useLayoutStore.getState().resolvedTheme).toBe('light');
    expect(persisted().theme).toBe('light');
    expect(document.documentElement.classList.contains('dark')).toBe(false);
  });

  it('resolves "system" against the OS setting', () => {
    useLayoutStore.getState().setTheme('system');
    expect(persisted().theme).toBe('system');
    expect(['dark', 'light']).toContain(useLayoutStore.getState().resolvedTheme);
  });

  it('never persists the resolved theme, which is derived', () => {
    useLayoutStore.getState().setTheme('dark');
    expect(persisted()).not.toHaveProperty('resolvedTheme');
  });

  it('writes the resolved value to the theme cookie', () => {
    useLayoutStore.getState().setTheme('dark');
    expect(document.cookie).toContain('orkyo-theme=dark');

    useLayoutStore.getState().setTheme('light');
    expect(document.cookie).toContain('orkyo-theme=light');
  });

  it('writes a resolved value — never "system" — to the cookie', () => {
    useLayoutStore.getState().setTheme('system');
    expect(/orkyo-theme=(dark|light)/.exec(document.cookie)).not.toBeNull();
  });
});

describe('useLayoutStore — OS theme changes', () => {
  function mockMatchMedia() {
    const listeners: ((e: { matches: boolean }) => void)[] = [];
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      configurable: true,
      value: vi.fn().mockReturnValue({
        matches: false,
        media: '(prefers-color-scheme: dark)',
        addEventListener: (_: string, cb: (e: { matches: boolean }) => void) => listeners.push(cb),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      }),
    });
    return listeners;
  }

  it('follows the OS while the preference is "system"', () => {
    const listeners = mockMatchMedia();
    useLayoutStore.getState().setTheme('system');
    listeners.forEach((cb) => { cb({ matches: true }); });
    expect(['dark', 'light']).toContain(useLayoutStore.getState().resolvedTheme);
  });

  it('ignores the OS once the preference is explicit', () => {
    const listeners = mockMatchMedia();
    useLayoutStore.getState().setTheme('dark');
    const resolved = useLayoutStore.getState().resolvedTheme;
    listeners.forEach((cb) => { cb({ matches: false }); });
    expect(useLayoutStore.getState().resolvedTheme).toBe(resolved);
  });
});
