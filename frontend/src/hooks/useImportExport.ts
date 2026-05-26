/**
 * React hooks for handling import/export actions fired from TopBar.
 * Pages subscribe via the ui-actions Zustand store; each tick increment
 * means a fresh trigger to consume.
 */

import { useEffect, useRef } from 'react';
import type { ExportFormat, ImportFormat, ExportContext } from '@foundation/src/lib/utils/import-export';
import { useUiActionsStore } from '@foundation/src/store/ui-actions-store';

export function useExportHandler(
  context: ExportContext,
  handler: (format: ExportFormat) => void | Promise<void>,
) {
  const tick = useUiActionsStore((s) => s.exportTick);
  const payload = useUiActionsStore((s) => s.lastExport);
  const lastTickRef = useRef(tick);
  const handlerRef = useRef(handler);
  handlerRef.current = handler;

  useEffect(() => {
    if (tick === lastTickRef.current) return;
    lastTickRef.current = tick;
    if (payload?.context === context) {
      void handlerRef.current(payload.format);
    }
  }, [tick, payload, context]);
}

export function useImportHandler(
  context: ExportContext,
  handler: (file: File, format: ImportFormat) => void | Promise<void>,
) {
  const tick = useUiActionsStore((s) => s.importTick);
  const payload = useUiActionsStore((s) => s.lastImport);
  const lastTickRef = useRef(tick);
  const handlerRef = useRef(handler);
  handlerRef.current = handler;

  useEffect(() => {
    if (tick === lastTickRef.current) return;
    lastTickRef.current = tick;
    if (payload?.context === context) {
      void handlerRef.current(payload.file, payload.format);
    }
  }, [tick, payload, context]);
}
