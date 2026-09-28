import { describe, it, expect, beforeEach } from 'vitest';
import { useUiActionsStore } from './ui-actions-store';
import type { ExportPayload, ImportPayload } from './ui-actions-store';

function resetStore() {
  useUiActionsStore.setState({
    exportTick: 0,
    importTick: 0,
    commandPaletteOpen: false,
    tourOpen: false,
    assistantOpen: false,
    scannerOpen: false,
    assistantContext: null,
    lastExport: null,
    lastImport: null,
  });
}

describe('useUiActionsStore', () => {
  beforeEach(resetStore);

  describe('triggerExport', () => {
    it('increments exportTick and stores payload', () => {
      const payload: ExportPayload = { context: 'spaces', format: 'csv' };
      useUiActionsStore.getState().triggerExport(payload);
      const s = useUiActionsStore.getState();
      expect(s.exportTick).toBe(1);
      expect(s.lastExport).toEqual(payload);
    });

    it('increments on each call', () => {
      useUiActionsStore.getState().triggerExport({ context: 'spaces', format: 'csv' });
      useUiActionsStore.getState().triggerExport({ context: 'spaces', format: 'json' });
      expect(useUiActionsStore.getState().exportTick).toBe(2);
    });

    it('does not affect other ticks', () => {
      useUiActionsStore.getState().triggerExport({ context: 'spaces', format: 'csv' });
      const s = useUiActionsStore.getState();
      expect(s.importTick).toBe(0);
      expect(s.commandPaletteOpen).toBe(false);
      expect(s.tourOpen).toBe(false);
    });
  });

  describe('triggerImport', () => {
    it('increments importTick and stores payload', () => {
      const file = new File([''], 'test.csv');
      const payload: ImportPayload = { context: 'spaces', format: 'csv', file };
      useUiActionsStore.getState().triggerImport(payload);
      const s = useUiActionsStore.getState();
      expect(s.importTick).toBe(1);
      expect(s.lastImport).toEqual(payload);
    });

    it('does not affect other ticks', () => {
      useUiActionsStore.getState().triggerImport({ context: 'spaces', format: 'csv', file: new File([''], 'f.csv') });
      const s = useUiActionsStore.getState();
      expect(s.exportTick).toBe(0);
      expect(s.commandPaletteOpen).toBe(false);
      expect(s.tourOpen).toBe(false);
    });
  });

  describe('command palette', () => {
    it('opens, and stays open when opened again', () => {
      useUiActionsStore.getState().openCommandPalette();
      useUiActionsStore.getState().openCommandPalette();
      expect(useUiActionsStore.getState().commandPaletteOpen).toBe(true);
    });

    it('toggles', () => {
      useUiActionsStore.getState().toggleCommandPalette();
      expect(useUiActionsStore.getState().commandPaletteOpen).toBe(true);
      useUiActionsStore.getState().toggleCommandPalette();
      expect(useUiActionsStore.getState().commandPaletteOpen).toBe(false);
    });

    it('closes through setCommandPaletteOpen', () => {
      useUiActionsStore.getState().openCommandPalette();
      useUiActionsStore.getState().setCommandPaletteOpen(false);
      expect(useUiActionsStore.getState().commandPaletteOpen).toBe(false);
    });

    it('does not open the other overlays', () => {
      useUiActionsStore.getState().openCommandPalette();
      const s = useUiActionsStore.getState();
      expect(s.tourOpen).toBe(false);
      expect(s.assistantOpen).toBe(false);
      expect(s.scannerOpen).toBe(false);
    });
  });

  describe('tour', () => {
    it('opens and closes', () => {
      useUiActionsStore.getState().openTour();
      expect(useUiActionsStore.getState().tourOpen).toBe(true);
      useUiActionsStore.getState().setTourOpen(false);
      expect(useUiActionsStore.getState().tourOpen).toBe(false);
    });
  });

  describe('assistant', () => {
    it('opens with the conflict it was asked about', () => {
      useUiActionsStore.getState().openAssistant({ type: 'conflict', requestId: 'r1' });
      const s = useUiActionsStore.getState();
      expect(s.assistantOpen).toBe(true);
      expect(s.assistantContext).toEqual({ type: 'conflict', requestId: 'r1' });
    });

    it('drops an earlier context when opened from the toolbar', () => {
      useUiActionsStore.getState().openAssistant({ type: 'conflict', requestId: 'r1' });
      useUiActionsStore.getState().setAssistantOpen(false);
      useUiActionsStore.getState().openAssistant();
      const s = useUiActionsStore.getState();
      expect(s.assistantOpen).toBe(true);
      expect(s.assistantContext).toBeNull();
    });
  });

  describe('scanner', () => {
    it('opens and closes', () => {
      useUiActionsStore.getState().openScanner();
      expect(useUiActionsStore.getState().scannerOpen).toBe(true);
      useUiActionsStore.getState().setScannerOpen(false);
      expect(useUiActionsStore.getState().scannerOpen).toBe(false);
    });
  });

  describe('auto-schedule', () => {
    it('holds the proposed ids until cleared', () => {
      useUiActionsStore.getState().requestAutoSchedule(['a', 'b']);
      expect(useUiActionsStore.getState().autoScheduleRequestIds).toEqual(['a', 'b']);
      useUiActionsStore.getState().clearAutoSchedule();
      expect(useUiActionsStore.getState().autoScheduleRequestIds).toBeNull();
    });
  });
});
