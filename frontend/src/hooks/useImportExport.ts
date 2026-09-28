/**
 * React hooks for handling import/export actions fired from TopBar.
 * Pages subscribe via the ui-actions Zustand store; each tick increment
 * means a fresh trigger to consume.
 */

import { useEffect, useEffectEvent, useRef } from 'react';
import { useMutation } from '@tanstack/react-query';
import { qk } from '@foundation/src/lib/api/query-keys';
import { REQUEST_DERIVED_QUERY_KEYS } from '@foundation/src/lib/core/invalidate-request-data';
import type { ExportFormat, ImportFormat, ExportContext } from '@foundation/src/lib/utils/import-export';
import { useUiActionsStore, type CalendarFeedCapability, type ExportCapability } from '@foundation/src/store/ui-actions-store';
import { useInvalidateKeys } from "@foundation/src/hooks/useInvalidateKeys";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";

/**
 * What the page tells the TopBar about itself when it registers — the store's
 * capability shape, reused rather than declaring a field-for-field twin (the
 * same treatment {@link useCalendarFeedHandler} already gives its type).
 */
export type ExportOffer = ExportCapability;

export function useExportHandler(
  context: ExportContext,
  handler: (format: ExportFormat) => void | Promise<void>,
  offer: ExportOffer,
) {
  const tick = useUiActionsStore((s) => s.exportTick);
  const payload = useUiActionsStore((s) => s.lastExport);
  const registerExport = useUiActionsStore((s) => s.registerExport);
  const unregisterExport = useUiActionsStore((s) => s.unregisterExport);
  const lastTickRef = useRef(tick);
  // A mutation, so a failed export reaches the central error toast instead of an unhandled
  // rejection. `mutationFn` is re-read on every render, so it always calls the latest handler.
  const exportMutation = useMutation({
    mutationFn: async (format: ExportFormat) => {
      await handler(format);
    },
    meta: { errorMessage: 'Export failed' },
  });
  // Effect event, not a ref: read only from the effect below, and must not retrigger it.
  const runExport = useEffectEvent((format: ExportFormat) => exportMutation.mutate(format));

  // Registering IS the offer: the TopBar enables Export for exactly as long as
  // this component is mounted, so a moved route can never silently orphan it.
  // Call sites pass array literals, so the formats are keyed by value.
  const formatsKey = offer.formats.join(',');
  const { label, description } = offer;
  useEffect(() => {
    registerExport(context, { label, description, formats: formatsKey.split(',') as ExportFormat[] });
    return () => unregisterExport(context);
  }, [context, label, description, formatsKey, registerExport, unregisterExport]);

  useEffect(() => {
    if (tick === lastTickRef.current) return;
    lastTickRef.current = tick;
    if (payload?.context === context) {
      runExport(payload.format);
    }
  }, [tick, payload, context]);
}

/**
 * Offers a live calendar subscription for as long as the page is mounted.
 *
 * No handler, unlike {@link useExportHandler}: the TopBar's dialog owns the
 * whole create/reveal/revoke flow, so registering is the entire contract. The
 * offer and the registered capability are the same shape, so it takes the
 * store's type rather than declaring a twin.
 */
export function useCalendarFeedHandler(
  context: ExportContext,
  offer: CalendarFeedCapability,
) {
  const registerCalendarFeed = useUiActionsStore((s) => s.registerCalendarFeed);
  const unregisterCalendarFeed = useUiActionsStore((s) => s.unregisterCalendarFeed);
  const { label, description } = offer;

  useEffect(() => {
    registerCalendarFeed(context, { label, description });
    return () => unregisterCalendarFeed(context);
  }, [context, label, description, registerCalendarFeed, unregisterCalendarFeed]);
}

/**
 * Import feedback, carried into the import mutation's `meta` (docs/dialog-feedback.md):
 * the handler does the work and throws on failure; the central MutationCache fires the
 * toast + query invalidation once, in one place.
 */
export interface ImportFeedbackOptions<T> {
  /** Success toast. A function receives the handler's return value (e.g. an imported count). */
  successMessage?: string | ((result: T) => string);
  /** Title for the error toast; the thrown error's message becomes the description. */
  errorMessage?: string;
  /** Query keys invalidated after a successful import, prefix-style (exact: false). */
  invalidates?: readonly (readonly unknown[])[];
  /** File formats this page accepts. Defaults to CSV. */
  formats?: ImportFormat[];
}

export function useImportHandler<T = void>(
  context: ExportContext,
  handler: (file: File, format: ImportFormat) => T | Promise<T>,
  options: ImportFeedbackOptions<T>,
) {
  // An import writes records, so a Viewer is never offered one: the page does not register,
  // and the TopBar has no Import entry to show.
  const canEdit = useCanEdit();
  const tick = useUiActionsStore((s) => s.importTick);
  const payload = useUiActionsStore((s) => s.lastImport);
  const registerImport = useUiActionsStore((s) => s.registerImport);
  const unregisterImport = useUiActionsStore((s) => s.unregisterImport);
  const lastTickRef = useRef(tick);
  // The central MutationCache fires the toast and the invalidation from `meta`
  // (docs/dialog-feedback.md); the options only translate into it.
  const { successMessage } = options;
  const importMutation = useMutation({
    mutationFn: ({ file, format }: { file: File; format: ImportFormat }) =>
      Promise.resolve(handler(file, format)),
    meta: {
      successMessage:
        typeof successMessage === 'function' ? (data) => successMessage(data as T) : successMessage,
      errorMessage: options.errorMessage ?? 'Import failed',
      invalidates: options.invalidates,
    },
  });
  // Effect event: the tick effect below must not depend on the mutation object.
  const runImport = useEffectEvent((file: File, format: ImportFormat) =>
    importMutation.mutate({ file, format }),
  );

  const importFormatsKey = (options.formats ?? ['csv']).join(',');
  useEffect(() => {
    if (!canEdit) return;
    registerImport(context, { formats: importFormatsKey.split(',') as ImportFormat[] });
    return () => unregisterImport(context);
  }, [canEdit, context, importFormatsKey, registerImport, unregisterImport]);

  useEffect(() => {
    if (tick === lastTickRef.current) return;
    lastTickRef.current = tick;
    if (!canEdit || payload?.context !== context) return;

    runImport(payload.file, payload.format);
  }, [canEdit, tick, payload, context]);
}

/**
 * Re-read everything a spreadsheet import can have written: resources, and every request feed.
 *
 * For an importer that creates records one call at a time rather than through a mutation, so
 * there is no `meta` to carry the invalidation. It runs after a partial failure too — whatever
 * was written before the error is real, and the screen has to show it.
 */
export function useInvalidateImportedData(): () => Promise<void> {
  return useInvalidateKeys(qk.resources.all(), ...REQUEST_DERIVED_QUERY_KEYS);
}
