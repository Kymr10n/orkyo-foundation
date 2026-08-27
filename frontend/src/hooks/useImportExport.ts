/**
 * React hooks for handling import/export actions fired from TopBar.
 * Pages subscribe via the ui-actions Zustand store; each tick increment
 * means a fresh trigger to consume.
 */

import { useEffect, useEffectEvent, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import type { ExportFormat, ImportFormat, ExportContext } from '@foundation/src/lib/utils/import-export';
import { useUiActionsStore, type CalendarFeedCapability, type ExportCapability } from '@foundation/src/store/ui-actions-store';

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
  // Effect event, not a ref: read only from the effect below, and must see the latest
  // handler without retriggering on every render that passes a new inline closure.
  const runExport = useEffectEvent((format: ExportFormat) => handler(format));

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
      void runExport(payload.format);
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
  /** File formats this page accepts. Defaults to CSV. */
  formats?: ImportFormat[];
}

export function useImportHandler<T = void>(
  context: ExportContext,
  handler: (file: File, format: ImportFormat) => T | Promise<T>,
  options?: ImportFeedbackOptions<T>,
) {
  const queryClient = useQueryClient();
  const tick = useUiActionsStore((s) => s.importTick);
  const payload = useUiActionsStore((s) => s.lastImport);
  const registerImport = useUiActionsStore((s) => s.registerImport);
  const unregisterImport = useUiActionsStore((s) => s.unregisterImport);
  const lastTickRef = useRef(tick);
  // The whole import run is an effect event: it must see the latest handler and options
  // without either becoming a dependency of the tick effect below.
  const runImport = useEffectEvent(async (file: File, format: ImportFormat) => {
    const opts = options;
    try {
      const result = await handler(file, format);
      if (!opts) return; // legacy consumers own their feedback
      opts.invalidates?.forEach((queryKey) => {
        void queryClient.invalidateQueries({ queryKey, exact: false });
      });
      const successMessage =
        typeof opts.successMessage === 'function' ? opts.successMessage(result) : opts.successMessage;
      if (successMessage) toast.success(successMessage);
    } catch (error) {
      if (!opts) throw error; // preserve legacy behavior (handler's own try/catch)
      toast.error(opts.errorMessage ?? 'Import failed', {
        description: error instanceof Error ? error.message : undefined,
      });
    }
  });

  const importFormatsKey = (options?.formats ?? ['csv']).join(',');
  useEffect(() => {
    registerImport(context, { formats: importFormatsKey.split(',') as ImportFormat[] });
    return () => unregisterImport(context);
  }, [context, importFormatsKey, registerImport, unregisterImport]);

  useEffect(() => {
    if (tick === lastTickRef.current) return;
    lastTickRef.current = tick;
    if (payload?.context !== context) return;

    void runImport(payload.file, payload.format);
  }, [tick, payload, context]);
}
