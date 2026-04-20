/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useCommandPalette } from './useCommandPalette';

describe('useCommandPalette', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    // Clean up any event listeners
    vi.restoreAllMocks();
  });

  describe('state management', () => {
    it('starts with isOpen = false', () => {
      const { result } = renderHook(() => useCommandPalette());
      expect(result.current.isOpen).toBe(false);
    });

    it('open() sets isOpen to true', () => {
      const { result } = renderHook(() => useCommandPalette());
      
      act(() => {
        result.current.open();
      });
      
      expect(result.current.isOpen).toBe(true);
    });

    it('close() sets isOpen to false', () => {
      const { result } = renderHook(() => useCommandPalette());
      
      act(() => {
        result.current.open();
      });
      expect(result.current.isOpen).toBe(true);
      
      act(() => {
        result.current.close();
      });
      expect(result.current.isOpen).toBe(false);
    });

    it('setIsOpen directly sets state', () => {
      const { result } = renderHook(() => useCommandPalette());
      
      act(() => {
        result.current.setIsOpen(true);
      });
      expect(result.current.isOpen).toBe(true);
      
      act(() => {
        result.current.setIsOpen(false);
      });
      expect(result.current.isOpen).toBe(false);
    });
  });

  describe('keyboard shortcut', () => {
    it('opens on Ctrl+K', () => {
      const { result } = renderHook(() => useCommandPalette());
      
      act(() => {
        const event = new KeyboardEvent('keydown', {
          key: 'k',
          ctrlKey: true,
          bubbles: true,
        });
        document.dispatchEvent(event);
      });
      
      expect(result.current.isOpen).toBe(true);
    });

    it('opens on Cmd+K (Meta)', () => {
      const { result } = renderHook(() => useCommandPalette());
      
      act(() => {
        const event = new KeyboardEvent('keydown', {
          key: 'k',
          metaKey: true,
          bubbles: true,
        });
        document.dispatchEvent(event);
      });
      
      expect(result.current.isOpen).toBe(true);
    });

    it('toggles state on repeated Ctrl+K', () => {
      const { result } = renderHook(() => useCommandPalette());
      
      const dispatchCtrlK = () => {
        const event = new KeyboardEvent('keydown', {
          key: 'k',
          ctrlKey: true,
          bubbles: true,
        });
        document.dispatchEvent(event);
      };
      
      // First press - opens
      act(() => {
        dispatchCtrlK();
      });
      expect(result.current.isOpen).toBe(true);
      
      // Second press - closes
      act(() => {
        dispatchCtrlK();
      });
      expect(result.current.isOpen).toBe(false);
    });

    it('does not open on just K without modifier', () => {
      const { result } = renderHook(() => useCommandPalette());
      
      act(() => {
        const event = new KeyboardEvent('keydown', {
          key: 'k',
          bubbles: true,
        });
        document.dispatchEvent(event);
      });
      
      expect(result.current.isOpen).toBe(false);
    });

    it('does not open on Ctrl with different key', () => {
      const { result } = renderHook(() => useCommandPalette());
      
      act(() => {
        const event = new KeyboardEvent('keydown', {
          key: 'j',
          ctrlKey: true,
          bubbles: true,
        });
        document.dispatchEvent(event);
      });
      
      expect(result.current.isOpen).toBe(false);
    });
  });

  describe('cleanup', () => {
    it('removes event listener on unmount', () => {
      const removeEventListenerSpy = vi.spyOn(document, 'removeEventListener');
      
      const { unmount } = renderHook(() => useCommandPalette());
      unmount();
      
      expect(removeEventListenerSpy).toHaveBeenCalledWith('keydown', expect.any(Function));
    });
  });
});
