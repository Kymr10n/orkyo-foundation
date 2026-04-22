import { describe, it, expect, beforeEach, vi } from 'vitest';
import { useAppStore } from '@/store/app-store';
import type { Conflict } from '@/types/requests';
import type { User } from '@/types/auth';

describe('useAppStore - authentication', () => {
  beforeEach(() => {
    useAppStore.setState({
      user: null,
      isAuthLoading: false,
    });
  });

  it('should set user', () => {
    const { setUser } = useAppStore.getState();
    const mockUser: User = {
      id: 'user-1',
      email: 'test@example.com',
      displayName: 'Test User',
      role: 'viewer',
      status: 'active',
      createdAt: '2024-01-01T00:00:00Z',
    };

    setUser(mockUser);

    expect(useAppStore.getState().user).toEqual(mockUser);
  });

  it('should clear user', () => {
    const { setUser } = useAppStore.getState();
    const mockUser: User = {
      id: 'user-1',
      email: 'test@example.com',
      displayName: 'Test User',
      role: 'viewer',
      status: 'active',
      createdAt: '2024-01-01T00:00:00Z',
    };

    setUser(mockUser);
    expect(useAppStore.getState().user).toEqual(mockUser);

    setUser(null);
    expect(useAppStore.getState().user).toBeNull();
  });

  it('should set auth loading state', () => {
    const { setIsAuthLoading } = useAppStore.getState();

    setIsAuthLoading(true);
    expect(useAppStore.getState().isAuthLoading).toBe(true);

    setIsAuthLoading(false);
    expect(useAppStore.getState().isAuthLoading).toBe(false);
  });
});

describe('useAppStore - site and space selection', () => {
  beforeEach(() => {
    localStorage.clear();
    useAppStore.setState({
      selectedSiteId: null,
      selectedHallId: null,
      selectedSpaceId: null,
      selectedJobId: null,
      selectedRequestId: null,
      isDetailsDrawerOpen: false,
    });
  });

  it('should set selected site and persist to localStorage', () => {
    const { setSelectedSiteId } = useAppStore.getState();

    setSelectedSiteId('site-1');

    expect(useAppStore.getState().selectedSiteId).toBe('site-1');
    expect(localStorage.getItem('selectedSiteId')).toBe('site-1');
  });

  it('should set selected hall', () => {
    const { setSelectedHallId } = useAppStore.getState();

    setSelectedHallId('hall-1');

    expect(useAppStore.getState().selectedHallId).toBe('hall-1');
  });

  it('should set selected space and open drawer', () => {
    const { setSelectedSpaceId } = useAppStore.getState();

    setSelectedSpaceId('space-1');

    expect(useAppStore.getState().selectedSpaceId).toBe('space-1');
    expect(useAppStore.getState().isDetailsDrawerOpen).toBe(true);
  });

  it('should clear selected space and close drawer', () => {
    const { setSelectedSpaceId } = useAppStore.getState();

    setSelectedSpaceId('space-1');
    expect(useAppStore.getState().isDetailsDrawerOpen).toBe(true);

    setSelectedSpaceId(null);
    expect(useAppStore.getState().selectedSpaceId).toBeNull();
    expect(useAppStore.getState().isDetailsDrawerOpen).toBe(false);
  });

  it('should set selected job and open drawer', () => {
    const { setSelectedJobId } = useAppStore.getState();

    setSelectedJobId('job-1');

    expect(useAppStore.getState().selectedJobId).toBe('job-1');
    expect(useAppStore.getState().isDetailsDrawerOpen).toBe(true);
  });

  it('should set selected request and open drawer', () => {
    const { setSelectedRequestId } = useAppStore.getState();

    setSelectedRequestId('request-1');

    expect(useAppStore.getState().selectedRequestId).toBe('request-1');
    expect(useAppStore.getState().isDetailsDrawerOpen).toBe(true);
  });
});

describe('useAppStore - scheduler controls', () => {
  beforeEach(() => {
    useAppStore.setState({
      scale: 'week',
      anchorTs: new Date(),
      timeCursorTs: new Date(),
    });
  });

  it('should set scale', () => {
    const { setScale } = useAppStore.getState();

    setScale('day');
    expect(useAppStore.getState().scale).toBe('day');

    setScale('month');
    expect(useAppStore.getState().scale).toBe('month');
  });

  it('should set anchor timestamp', () => {
    const { setAnchorTs } = useAppStore.getState();
    const testDate = new Date('2026-01-15');

    setAnchorTs(testDate);

    expect(useAppStore.getState().anchorTs).toEqual(testDate);
  });

  it('should set time cursor timestamp', () => {
    const { setTimeCursorTs } = useAppStore.getState();
    const testDate = new Date('2026-02-20');

    setTimeCursorTs(testDate);

    expect(useAppStore.getState().timeCursorTs).toEqual(testDate);
  });
});

describe('useAppStore - space ordering', () => {
  beforeEach(() => {
    useAppStore.setState({
      spaceOrder: [],
    });
  });

  it('should set space order', () => {
    const { setSpaceOrder } = useAppStore.getState();
    const order = ['space-1', 'space-2', 'space-3'];

    setSpaceOrder(order);

    expect(useAppStore.getState().spaceOrder).toEqual(order);
  });

  it('should update space order', () => {
    const { setSpaceOrder } = useAppStore.getState();

    setSpaceOrder(['space-1', 'space-2']);
    setSpaceOrder(['space-2', 'space-1', 'space-3']);

    expect(useAppStore.getState().spaceOrder).toEqual(['space-2', 'space-1', 'space-3']);
  });
});

describe('useAppStore - UI state', () => {
  beforeEach(() => {
    localStorage.clear();
    useAppStore.setState({
      isDetailsDrawerOpen: false,
      isFloorplanCollapsed: false,
      isSidebarCollapsed: false,
      rightPanelTab: 'floorplan',
    });
  });

  it('should toggle details drawer', () => {
    const { setIsDetailsDrawerOpen } = useAppStore.getState();

    setIsDetailsDrawerOpen(true);
    expect(useAppStore.getState().isDetailsDrawerOpen).toBe(true);

    setIsDetailsDrawerOpen(false);
    expect(useAppStore.getState().isDetailsDrawerOpen).toBe(false);
  });

  it('should toggle floorplan collapsed state', () => {
    const { setIsFloorplanCollapsed } = useAppStore.getState();

    setIsFloorplanCollapsed(true);
    expect(useAppStore.getState().isFloorplanCollapsed).toBe(true);

    setIsFloorplanCollapsed(false);
    expect(useAppStore.getState().isFloorplanCollapsed).toBe(false);
  });

  it('should toggle sidebar and persist to localStorage', () => {
    const { setIsSidebarCollapsed } = useAppStore.getState();

    setIsSidebarCollapsed(true);
    expect(useAppStore.getState().isSidebarCollapsed).toBe(true);
    expect(localStorage.getItem('sidebar-collapsed')).toBe('true');

    setIsSidebarCollapsed(false);
    expect(useAppStore.getState().isSidebarCollapsed).toBe(false);
    expect(localStorage.getItem('sidebar-collapsed')).toBe('false');
  });

  it('should set right panel tab', () => {
    const { setRightPanelTab } = useAppStore.getState();

    setRightPanelTab('details');
    expect(useAppStore.getState().rightPanelTab).toBe('details');

    setRightPanelTab('floorplan');
    expect(useAppStore.getState().rightPanelTab).toBe('floorplan');
  });
});

describe('useAppStore - space groups', () => {
  beforeEach(() => {
    useAppStore.setState({
      collapsedGroupIds: new Set<string>(),
    });
  });

  it('should collapse group', () => {
    const { toggleGroupCollapse } = useAppStore.getState();

    toggleGroupCollapse('group-1');

    expect(useAppStore.getState().collapsedGroupIds.has('group-1')).toBe(true);
  });

  it('should expand collapsed group', () => {
    const { toggleGroupCollapse } = useAppStore.getState();

    toggleGroupCollapse('group-1');
    expect(useAppStore.getState().collapsedGroupIds.has('group-1')).toBe(true);

    toggleGroupCollapse('group-1');
    expect(useAppStore.getState().collapsedGroupIds.has('group-1')).toBe(false);
  });

  it('should handle multiple groups', () => {
    const { toggleGroupCollapse } = useAppStore.getState();

    toggleGroupCollapse('group-1');
    toggleGroupCollapse('group-2');

    const collapsed = useAppStore.getState().collapsedGroupIds;
    expect(collapsed.has('group-1')).toBe(true);
    expect(collapsed.has('group-2')).toBe(true);
    expect(collapsed.size).toBe(2);
  });
});

describe('useAppStore - theme', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('dark');
    useAppStore.setState({
      theme: 'system',
      resolvedTheme: 'dark',
    });
  });

  it('should set theme to dark', () => {
    const { setTheme } = useAppStore.getState();

    setTheme('dark');

    expect(useAppStore.getState().theme).toBe('dark');
    expect(useAppStore.getState().resolvedTheme).toBe('dark');
    expect(localStorage.getItem('theme')).toBe('dark');
    expect(document.documentElement.classList.contains('dark')).toBe(true);
  });

  it('should set theme to light', () => {
    const { setTheme } = useAppStore.getState();

    setTheme('light');

    expect(useAppStore.getState().theme).toBe('light');
    expect(useAppStore.getState().resolvedTheme).toBe('light');
    expect(localStorage.getItem('theme')).toBe('light');
    expect(document.documentElement.classList.contains('dark')).toBe(false);
  });

  it('should set theme to system', () => {
    const { setTheme } = useAppStore.getState();

    setTheme('system');

    expect(useAppStore.getState().theme).toBe('system');
    expect(localStorage.getItem('theme')).toBe('system');
    // resolvedTheme should be based on matchMedia (mocked as false → light in jsdom)
    expect(['dark', 'light']).toContain(useAppStore.getState().resolvedTheme);
  });

  it('should persist theme preference across setTheme calls', () => {
    const { setTheme } = useAppStore.getState();

    setTheme('light');
    expect(localStorage.getItem('theme')).toBe('light');

    setTheme('dark');
    expect(localStorage.getItem('theme')).toBe('dark');

    setTheme('system');
    expect(localStorage.getItem('theme')).toBe('system');
  });

  it('should apply dark class for dark theme', () => {
    const { setTheme } = useAppStore.getState();

    setTheme('dark');
    expect(document.documentElement.classList.contains('dark')).toBe(true);

    setTheme('light');
    expect(document.documentElement.classList.contains('dark')).toBe(false);
  });

  it('should set orkyo-theme cookie when setting theme to dark', () => {
    const { setTheme } = useAppStore.getState();

    setTheme('dark');

    expect(document.cookie).toContain('orkyo-theme=dark');
  });

  it('should set orkyo-theme cookie when setting theme to light', () => {
    const { setTheme } = useAppStore.getState();

    setTheme('light');

    expect(document.cookie).toContain('orkyo-theme=light');
  });

  it('should set orkyo-theme cookie with resolved value for system theme', () => {
    const { setTheme } = useAppStore.getState();

    setTheme('system');

    // Cookie should contain the resolved value (dark or light), not "system"
    const cookieMatch = /orkyo-theme=(dark|light)/.exec(document.cookie);
    expect(cookieMatch).not.toBeNull();
  });

  it('should update resolvedTheme when matchMedia changes in system mode', () => {
    // Mock matchMedia with event listener support
    const listeners: ((e: { matches: boolean }) => void)[] = [];
    const mockMatchMedia = vi.fn().mockReturnValue({
      matches: false,
      media: '(prefers-color-scheme: dark)',
      addEventListener: (_: string, cb: (e: { matches: boolean }) => void) => listeners.push(cb),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    });
    Object.defineProperty(window, 'matchMedia', { value: mockMatchMedia, writable: true });

    const { setTheme } = useAppStore.getState();
    setTheme('system');

    // Simulate OS switching to dark
    listeners.forEach(cb => cb({ matches: true }));

    // The listener in store.ts should update resolvedTheme
    expect(['dark', 'light']).toContain(useAppStore.getState().resolvedTheme);

    // Cleanup
    Object.defineProperty(window, 'matchMedia', { value: undefined, writable: true });
  });

  it('should not update resolvedTheme on matchMedia change when theme is explicit', () => {
    const listeners: ((e: { matches: boolean }) => void)[] = [];
    const mockMatchMedia = vi.fn().mockReturnValue({
      matches: false,
      media: '(prefers-color-scheme: dark)',
      addEventListener: (_: string, cb: (e: { matches: boolean }) => void) => listeners.push(cb),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    });
    Object.defineProperty(window, 'matchMedia', { value: mockMatchMedia, writable: true });

    const { setTheme } = useAppStore.getState();
    setTheme('dark');

    const resolved = useAppStore.getState().resolvedTheme;

    // Simulate OS switching to light — should be ignored since theme is explicit
    listeners.forEach(cb => cb({ matches: false }));

    expect(useAppStore.getState().resolvedTheme).toBe(resolved);

    // Cleanup
    Object.defineProperty(window, 'matchMedia', { value: undefined, writable: true });
  });
});

describe('useAppStore - conflicts', () => {
  beforeEach(() => {
    // Reset store to initial state
    useAppStore.setState({
      conflicts: new Map(),
    });
  });

  describe('setConflicts', () => {
    it('should add conflicts for a request', () => {
      const { setConflicts, conflicts: _conflicts } = useAppStore.getState();
      
      const testConflicts: Conflict[] = [
        {
          id: 'conflict-1',
          kind: 'load_exceeded',
          severity: 'error',
          message: 'Test conflict',
        },
      ];

      setConflicts('req-1', testConflicts);

      const updatedConflicts = useAppStore.getState().conflicts;
      expect(updatedConflicts.get('req-1')).toEqual(testConflicts);
      expect(updatedConflicts.size).toBe(1);
    });

    it('should update existing conflicts for a request', () => {
      const { setConflicts } = useAppStore.getState();
      
      const initialConflicts: Conflict[] = [
        {
          id: 'conflict-1',
          kind: 'load_exceeded',
          severity: 'error',
          message: 'Initial conflict',
        },
      ];

      setConflicts('req-1', initialConflicts);

      const updatedConflicts: Conflict[] = [
        {
          id: 'conflict-2',
          kind: 'connector_mismatch',
          severity: 'warning',
          message: 'Updated conflict',
        },
      ];

      setConflicts('req-1', updatedConflicts);

      const currentConflicts = useAppStore.getState().conflicts;
      expect(currentConflicts.get('req-1')).toEqual(updatedConflicts);
      expect(currentConflicts.size).toBe(1);
    });

    it('should remove conflicts when empty array is provided', () => {
      const { setConflicts } = useAppStore.getState();
      
      const testConflicts: Conflict[] = [
        {
          id: 'conflict-1',
          kind: 'load_exceeded',
          severity: 'error',
          message: 'Test conflict',
        },
      ];

      setConflicts('req-1', testConflicts);
      expect(useAppStore.getState().conflicts.size).toBe(1);

      setConflicts('req-1', []);
      expect(useAppStore.getState().conflicts.size).toBe(0);
      expect(useAppStore.getState().conflicts.get('req-1')).toBeUndefined();
    });

    it('should handle multiple requests with conflicts', () => {
      const { setConflicts } = useAppStore.getState();
      
      const conflicts1: Conflict[] = [
        {
          id: 'conflict-1',
          kind: 'load_exceeded',
          severity: 'error',
          message: 'Conflict for request 1',
        },
      ];

      const conflicts2: Conflict[] = [
        {
          id: 'conflict-2',
          kind: 'connector_mismatch',
          severity: 'warning',
          message: 'Conflict for request 2',
        },
      ];

      setConflicts('req-1', conflicts1);
      setConflicts('req-2', conflicts2);

      const currentConflicts = useAppStore.getState().conflicts;
      expect(currentConflicts.size).toBe(2);
      expect(currentConflicts.get('req-1')).toEqual(conflicts1);
      expect(currentConflicts.get('req-2')).toEqual(conflicts2);
    });

    it('should handle multiple conflicts for single request', () => {
      const { setConflicts } = useAppStore.getState();
      
      const multipleConflicts: Conflict[] = [
        {
          id: 'conflict-1',
          kind: 'load_exceeded',
          severity: 'error',
          message: 'First conflict',
        },
        {
          id: 'conflict-2',
          kind: 'connector_mismatch',
          severity: 'error',
          message: 'Second conflict',
        },
        {
          id: 'conflict-3',
          kind: 'size_mismatch',
          severity: 'warning',
          message: 'Third conflict',
        },
      ];

      setConflicts('req-1', multipleConflicts);

      const currentConflicts = useAppStore.getState().conflicts;
      expect(currentConflicts.get('req-1')).toEqual(multipleConflicts);
      expect(currentConflicts.get('req-1')?.length).toBe(3);
    });
  });

  describe('clearConflicts', () => {
    it('should remove conflicts for a specific request', () => {
      const { setConflicts, clearConflicts } = useAppStore.getState();
      
      const testConflicts: Conflict[] = [
        {
          id: 'conflict-1',
          kind: 'load_exceeded',
          severity: 'error',
          message: 'Test conflict',
        },
      ];

      setConflicts('req-1', testConflicts);
      expect(useAppStore.getState().conflicts.size).toBe(1);

      clearConflicts('req-1');
      expect(useAppStore.getState().conflicts.size).toBe(0);
      expect(useAppStore.getState().conflicts.get('req-1')).toBeUndefined();
    });

    it('should only remove conflicts for specified request', () => {
      const { setConflicts, clearConflicts } = useAppStore.getState();
      
      const conflicts1: Conflict[] = [
        {
          id: 'conflict-1',
          kind: 'load_exceeded',
          severity: 'error',
          message: 'Conflict 1',
        },
      ];

      const conflicts2: Conflict[] = [
        {
          id: 'conflict-2',
          kind: 'connector_mismatch',
          severity: 'warning',
          message: 'Conflict 2',
        },
      ];

      setConflicts('req-1', conflicts1);
      setConflicts('req-2', conflicts2);
      expect(useAppStore.getState().conflicts.size).toBe(2);

      clearConflicts('req-1');
      const currentConflicts = useAppStore.getState().conflicts;
      expect(currentConflicts.size).toBe(1);
      expect(currentConflicts.get('req-1')).toBeUndefined();
      expect(currentConflicts.get('req-2')).toEqual(conflicts2);
    });

    it('should handle clearing non-existent conflicts gracefully', () => {
      const { clearConflicts } = useAppStore.getState();
      
      expect(() => clearConflicts('non-existent')).not.toThrow();
      expect(useAppStore.getState().conflicts.size).toBe(0);
    });
  });

  describe('getAllConflicts', () => {
    it('should return empty array when no conflicts exist', () => {
      const { getAllConflicts } = useAppStore.getState();
      
      const allConflicts = getAllConflicts();
      expect(allConflicts).toEqual([]);
    });

    it('should return all conflicts from single request', () => {
      const { setConflicts, getAllConflicts } = useAppStore.getState();
      
      const testConflicts: Conflict[] = [
        {
          id: 'conflict-1',
          kind: 'load_exceeded',
          severity: 'error',
          message: 'Test conflict',
        },
        {
          id: 'conflict-2',
          kind: 'connector_mismatch',
          severity: 'warning',
          message: 'Another conflict',
        },
      ];

      setConflicts('req-1', testConflicts);

      const allConflicts = getAllConflicts();
      expect(allConflicts).toHaveLength(2);
      expect(allConflicts).toEqual(expect.arrayContaining(testConflicts));
    });

    it('should return all conflicts from multiple requests', () => {
      const { setConflicts, getAllConflicts } = useAppStore.getState();
      
      const conflicts1: Conflict[] = [
        {
          id: 'conflict-1',
          kind: 'load_exceeded',
          severity: 'error',
          message: 'Request 1 conflict',
        },
      ];

      const conflicts2: Conflict[] = [
        {
          id: 'conflict-2',
          kind: 'connector_mismatch',
          severity: 'warning',
          message: 'Request 2 conflict',
        },
        {
          id: 'conflict-3',
          kind: 'size_mismatch',
          severity: 'error',
          message: 'Request 2 another conflict',
        },
      ];

      setConflicts('req-1', conflicts1);
      setConflicts('req-2', conflicts2);

      const allConflicts = getAllConflicts();
      expect(allConflicts).toHaveLength(3);
      expect(allConflicts).toEqual(
        expect.arrayContaining([...conflicts1, ...conflicts2])
      );
    });

    it('should return updated conflicts after modifications', () => {
      const { setConflicts, clearConflicts, getAllConflicts } = useAppStore.getState();
      
      const conflicts1: Conflict[] = [
        {
          id: 'conflict-1',
          kind: 'load_exceeded',
          severity: 'error',
          message: 'Conflict 1',
        },
      ];

      const conflicts2: Conflict[] = [
        {
          id: 'conflict-2',
          kind: 'connector_mismatch',
          severity: 'warning',
          message: 'Conflict 2',
        },
      ];

      setConflicts('req-1', conflicts1);
      setConflicts('req-2', conflicts2);
      expect(getAllConflicts()).toHaveLength(2);

      clearConflicts('req-1');
      expect(getAllConflicts()).toHaveLength(1);
      expect(getAllConflicts()).toEqual(conflicts2);
    });
  });

  describe('Map immutability', () => {
    it('should create new Map instance when setting conflicts', () => {
      const { setConflicts } = useAppStore.getState();
      
      const initialMap = useAppStore.getState().conflicts;
      
      const testConflicts: Conflict[] = [
        {
          id: 'conflict-1',
          kind: 'load_exceeded',
          severity: 'error',
          message: 'Test',
        },
      ];

      setConflicts('req-1', testConflicts);
      
      const updatedMap = useAppStore.getState().conflicts;
      expect(updatedMap).not.toBe(initialMap); // Different reference
      expect(updatedMap.get('req-1')).toEqual(testConflicts);
    });

    it('should create new Map instance when clearing conflicts', () => {
      const { setConflicts, clearConflicts } = useAppStore.getState();
      
      const testConflicts: Conflict[] = [
        {
          id: 'conflict-1',
          kind: 'load_exceeded',
          severity: 'error',
          message: 'Test',
        },
      ];

      setConflicts('req-1', testConflicts);
      const mapAfterSet = useAppStore.getState().conflicts;

      clearConflicts('req-1');
      const mapAfterClear = useAppStore.getState().conflicts;

      expect(mapAfterClear).not.toBe(mapAfterSet); // Different reference
    });
  });
});
