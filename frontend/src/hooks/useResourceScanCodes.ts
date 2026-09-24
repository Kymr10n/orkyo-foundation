import { useMutation, useQuery } from "@tanstack/react-query";
import {
  getResourceScanCodes,
  linkResourceScanCode,
  unlinkResourceScanCode,
} from "@foundation/src/lib/api/resource-scan-codes-api";
import { getResourceStatus } from "@foundation/src/lib/api/resource-status-api";
import { getResources } from "@foundation/src/lib/api/resources-api";
import { useResourceTypes } from "@foundation/src/hooks/useResourceTypes";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";

/** The QR sticker codes one resource carries. */
export const useResourceScanCodes = (resourceId: string, enabled = true) =>
  useQuery({
    queryKey: qk.resources.scanCodes(resourceId),
    queryFn: () => getResourceScanCodes(resourceId),
    staleTime: STALE.OPERATIONAL,
    enabled,
  });

export interface LinkScanCodeVariables {
  resourceId: string;
  code: string;
  /** Takes the code from the resource that carries it now. */
  move?: boolean;
}

/**
 * Links a scanned code to a resource, or moves it there from another resource.
 * `inlineErrors` is for a dialog that stays open and shows the failure itself.
 */
export const useLinkResourceScanCode = ({ inlineErrors = false }: { inlineErrors?: boolean } = {}) =>
  useMutation({
    mutationFn: ({ resourceId, code, move }: LinkScanCodeVariables) =>
      linkResourceScanCode(resourceId, code, move ?? false),
    meta: {
      successMessage: (_data: unknown, vars: unknown) =>
        (vars as LinkScanCodeVariables).move ? "QR code moved" : "QR code linked",
      errorMessage: "Failed to link QR code",
      suppressErrorToast: inlineErrors,
      // A move empties another resource's list, so refresh every list, not only this one.
      invalidates: [qk.resources.scanCodesAll()],
    },
  });

/**
 * Active resources a scanned, unknown code can be linked to: those whose type has QR
 * codes turned on. Shares the flat resource list the availability-event picker loads.
 */
export const useScanLinkCandidates = (enabled: boolean) => {
  const { data: resources } = useQuery({
    queryKey: qk.resources.allFlat(),
    queryFn: () => getResources({ isActive: true }).then((r) => r.items),
    staleTime: STALE.OPERATIONAL,
    enabled,
  });
  const { data: types } = useResourceTypes();
  const scannable = new Map((types ?? []).filter((t) => t.scanCodesEnabled).map((t) => [t.key, t]));
  return (resources ?? [])
    .filter((r) => scannable.has(r.resourceTypeKey))
    .map((r) => ({ id: r.id, label: `${r.name} (${scannable.get(r.resourceTypeKey)!.displayName})` }));
};

export const useUnlinkResourceScanCode = (resourceId: string) =>
  useMutation({
    mutationFn: (codeId: string) => unlinkResourceScanCode(resourceId, codeId),
    meta: {
      successMessage: "QR code removed",
      errorMessage: "Failed to remove QR code",
      invalidates: [qk.resources.scanCodes(resourceId)],
    },
  });

/** One resource at a glance: bookings, absence, conflicts, utilization. */
export const useResourceStatus = (resourceId: string, enabled = true) =>
  useQuery({
    queryKey: qk.resources.status(resourceId),
    queryFn: () => getResourceStatus(resourceId),
    staleTime: STALE.REALTIME,
    enabled,
  });
