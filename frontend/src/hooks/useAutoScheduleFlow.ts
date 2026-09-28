import { useCallback, useEffect, useState } from "react";
import { addMonths, format } from "date-fns";
import {
  useApplyAutoSchedule,
  useAutoScheduleAvailable,
  usePreviewAutoSchedule,
} from "@foundation/src/hooks/useAutoSchedule";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";
import type { AutoSchedulePreviewResponse } from "@foundation/src/lib/api/auto-schedule-api";
import { ApiError } from "@foundation/src/lib/core/api-utils";
import { DATE_FORMATS } from "@foundation/src/lib/formatters";
import { useUiActionsStore } from "@foundation/src/store/ui-actions-store";

const AUTO_SCHEDULE_HORIZON_MONTHS = 3;

export const STALE_PREVIEW_MESSAGE =
  "The scheduling data has changed since this preview was generated. Please close and re-run the auto-schedule.";

export interface AutoScheduleScope {
  siteId: string | null;
  /** The horizon runs from this day for three months. */
  anchorTs: Date;
  /** The types the run fills; `undefined` means every type. */
  resourceTypeKeys: string[] | undefined;
}

/**
 * The scheduler's auto-schedule flow: preview → review in a dialog → apply.
 *
 * Two entry points open the same dialog: `start` (the toolbar button, a whole-site run) and an
 * accepted assistant proposal arriving through the ui-actions store (a run over exactly the
 * proposed requests). The toast and the request-data invalidation on apply come from
 * `useApplyAutoSchedule`'s `meta`; this hook keeps the dialog state and classifies a stale
 * preview (409) into the dialog's error.
 */
export function useAutoScheduleFlow({ siteId, anchorTs, resourceTypeKeys }: AutoScheduleScope) {
  const available = useAutoScheduleAvailable();
  const previewMutation = usePreviewAutoSchedule();
  const applyMutation = useApplyAutoSchedule();
  const [preview, setPreview] = useState<AutoSchedulePreviewResponse | null>(null);
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  /**
   * Set when the run was scoped to specific requests (an accepted assistant proposal),
   * null for the page's own whole-site run. Apply must repeat it, or the backend re-solves
   * over a wider set than the preview showed.
   */
  const [requestIds, setRequestIds] = useState<string[] | null>(null);
  const [applyError, setApplyError] = useState<string | null>(null);

  const horizonStart = format(anchorTs, DATE_FORMATS.DATE_ISO);
  const horizonEnd = format(addMonths(anchorTs, AUTO_SCHEDULE_HORIZON_MONTHS), DATE_FORMATS.DATE_ISO);

  const start = useCallback(async () => {
    if (!siteId) return;
    try {
      const result = await previewMutation.mutateAsync({
        siteId,
        horizonStart,
        horizonEnd,
        resourceTypeKeys,
      });
      setRequestIds(null);
      setPreview(result);
      setIsDialogOpen(true);
    } catch {
      // Error handled by mutation state
    }
  }, [siteId, horizonStart, horizonEnd, resourceTypeKeys, previewMutation]);

  // An accepted auto-scheduling proposal lands here: preview exactly the requests the
  // person approved and open the ordinary dialog. Keyed on the tick rather than the ids so
  // accepting the same proposal twice still fires, and so a re-render never re-runs it.
  const proposedRequestIds = useUiActionsStore((s) => s.autoScheduleRequestIds);
  const clearAutoSchedule = useUiActionsStore((s) => s.clearAutoSchedule);

  useEffect(() => {
    if (!siteId || !proposedRequestIds?.length) return;
    // Consumed here rather than remembered in a ref: a ref dies with the page, so coming
    // back to the scheduler later would re-open the preview for requests already dealt
    // with. Clearing the payload makes the request single-use wherever it is read.
    clearAutoSchedule();

    void (async () => {
      try {
        const result = await previewMutation.mutateAsync({
          siteId,
          horizonStart,
          horizonEnd,
          requestIds: proposedRequestIds,
          resourceTypeKeys,
        });
        setRequestIds(proposedRequestIds);
        setPreview(result);
        setIsDialogOpen(true);
      } catch {
        // Surfaced by the mutation's own error state, same as the toolbar run.
      }
    })();
  }, [proposedRequestIds, clearAutoSchedule, siteId, horizonStart, horizonEnd, resourceTypeKeys, previewMutation]);

  const apply = useCallback(async () => {
    if (!siteId) return;
    setApplyError(null);
    try {
      // resourceTypeKeys must be the set the preview solved for — the fingerprint alone
      // doesn't pin it, so a changed filter would re-solve for a different set.
      await applyMutation.mutateAsync({
        request: {
          siteId,
          horizonStart,
          horizonEnd,
          requestIds: requestIds ?? undefined,
          resourceTypeKeys,
          previewFingerprint: preview?.fingerprint,
        },
        scheduledCount: preview?.assignments.length ?? 0,
      });
      setIsDialogOpen(false);
      setPreview(null);
    } catch (err) {
      setApplyError(err instanceof ApiError && err.status === 409 ? STALE_PREVIEW_MESSAGE : errorMessage(err));
    }
  }, [siteId, horizonStart, horizonEnd, requestIds, resourceTypeKeys, applyMutation, preview]);

  const close = useCallback(() => {
    setIsDialogOpen(false);
    setPreview(null);
    setApplyError(null);
  }, []);

  return {
    available,
    isPreviewing: previewMutation.isPending,
    start,
    /** Spread onto `AutoSchedulePreviewDialog`. */
    dialog: {
      open: isDialogOpen,
      preview,
      isApplying: applyMutation.isPending,
      applyError,
      onApply: apply,
      onClose: close,
    },
  };
}
