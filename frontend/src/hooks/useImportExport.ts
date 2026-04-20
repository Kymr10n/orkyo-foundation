/**
 * React hook for handling import/export events from TopBar
 * Pages can use this hook to respond to import/export actions
 */

import { useEffect } from 'react';
import type { ExportFormat, ImportFormat, ExportContext } from '@/lib/utils/import-export';

interface ExportEvent {
  context: ExportContext;
  format: ExportFormat;
}

interface ImportEvent {
  context: ExportContext;
  format: ImportFormat;
  file: File;
}

export function useExportHandler(
  context: ExportContext,
  handler: (format: ExportFormat) => void | Promise<void>
) {
  useEffect(() => {
    const handleExport = (event: Event) => {
      const customEvent = event as CustomEvent<ExportEvent>;
      if (customEvent.detail.context === context) {
        handler(customEvent.detail.format);
      }
    };

    window.addEventListener('export-data', handleExport);
    return () => window.removeEventListener('export-data', handleExport);
  }, [context, handler]);
}

export function useImportHandler(
  context: ExportContext,
  handler: (file: File, format: ImportFormat) => void | Promise<void>
) {
  useEffect(() => {
    const handleImport = (event: Event) => {
      const customEvent = event as CustomEvent<ImportEvent>;
      if (customEvent.detail.context === context) {
        handler(customEvent.detail.file, customEvent.detail.format);
      }
    };

    window.addEventListener('import-data', handleImport);
    return () => window.removeEventListener('import-data', handleImport);
  }, [context, handler]);
}
