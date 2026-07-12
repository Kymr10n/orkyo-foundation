/**
 * React hooks for handling import/export actions fired from TopBar.
 * Pages subscribe via the ui-actions Zustand store; each tick increment
 * means a fresh trigger to consume.
 */

import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
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

/**
 * Centralized import feedback, mirroring the mutation `meta` convention
 * (docs/dialog-feedback.md): the handler does the work and throws on failure;
 * the hook fires the toast + query invalidation once, in one place.
 */
export interface ImportFeedbackOptions<T> {
  /** Success toast. A function receives the handler's return value (e.g. an imported count). */
  successMessage?: string | ((result: T) => string);
  /** Title for the error toast; the thrown error's message becomes the description. */
  errorMessage?: string;
  /** Query keys invalidated after a successful import, prefix-style (exact: false). */
  invalidates?: readonly (readonly unknown[])[];
}

export function useImportHandler<T = void>(
  context: ExportContext,
  handler: (file: File, format: ImportFormat) => T | Promise<T>,
  options?: ImportFeedbackOptions<T>,
) {
  const queryClient = useQueryClient();
  const tick = useUiActionsStore((s) => s.importTick);
  const payload = useUiActionsStore((s) => s.lastImport);
  const lastTickRef = useRef(tick);
  const handlerRef = useRef(handler);
  handlerRef.current = handler;
  const optionsRef = useRef(options);
  optionsRef.current = options;

  useEffect(() => {
    if (tick === lastTickRef.current) return;
    lastTickRef.current = tick;
    if (payload?.context !== context) return;

    void (async () => {
      const opts = optionsRef.current;
      try {
        const result = await handlerRef.current(payload.file, payload.format);
        if (!opts) return; // legacy consumers own their feedback
        opts.invalidates?.forEach((queryKey) => {
          void queryClient.invalidateQueries({ queryKey, exact: false });
        });
        const successMessage =
          typeof opts.successMessage === 'function'
            ? opts.successMessage(result)
            : opts.successMessage;
        if (successMessage) toast.success(successMessage);
      } catch (error) {
        const opts2 = optionsRef.current;
        if (!opts2) throw error; // preserve legacy behavior (handler's own try/catch)
        toast.error(opts2.errorMessage ?? 'Import failed', {
          description: error instanceof Error ? error.message : undefined,
        });
      }
    })();
  }, [tick, payload, context, queryClient]);
}
