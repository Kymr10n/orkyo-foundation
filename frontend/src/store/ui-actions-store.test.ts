import { describe, it, expect, beforeEach } from 'vitest';
import { useUiActionsStore } from './ui-actions-store';
import type { ExportPayload, ImportPayload } from './ui-actions-store';

function resetStore() {
  useUiActionsStore.setState({
    exportTick: 0,
    importTick: 0,
    commandPaletteTick: 0,
    tourTick: 0,
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
      expect(s.commandPaletteTick).toBe(0);
      expect(s.tourTick).toBe(0);
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
      expect(s.commandPaletteTick).toBe(0);
      expect(s.tourTick).toBe(0);
    });
  });

  describe('openCommandPalette', () => {
    it('increments commandPaletteTick', () => {
      useUiActionsStore.getState().openCommandPalette();
      expect(useUiActionsStore.getState().commandPaletteTick).toBe(1);
    });

    it('increments on each call', () => {
      useUiActionsStore.getState().openCommandPalette();
      useUiActionsStore.getState().openCommandPalette();
      expect(useUiActionsStore.getState().commandPaletteTick).toBe(2);
    });

    it('does not affect other ticks', () => {
      useUiActionsStore.getState().openCommandPalette();
      const s = useUiActionsStore.getState();
      expect(s.exportTick).toBe(0);
      expect(s.importTick).toBe(0);
      expect(s.tourTick).toBe(0);
    });
  });

  describe('openTour', () => {
    it('increments tourTick', () => {
      useUiActionsStore.getState().openTour();
      expect(useUiActionsStore.getState().tourTick).toBe(1);
    });

    it('increments on each call', () => {
      useUiActionsStore.getState().openTour();
      useUiActionsStore.getState().openTour();
      expect(useUiActionsStore.getState().tourTick).toBe(2);
    });

    it('does not affect other ticks', () => {
      useUiActionsStore.getState().openTour();
      const s = useUiActionsStore.getState();
      expect(s.exportTick).toBe(0);
      expect(s.importTick).toBe(0);
      expect(s.commandPaletteTick).toBe(0);
    });
  });
});
