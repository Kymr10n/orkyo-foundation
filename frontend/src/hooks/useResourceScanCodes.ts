import { useMutation, useQuery } from "@tanstack/react-query";
import {
  getResourceScanCodes,
  linkResourceScanCode,
  lookupScanCode,
  unlinkResourceScanCode,
} from "@foundation/src/lib/api/resource-scan-codes-api";
import { getResourceStatus } from "@foundation/src/lib/api/resource-status-api";
import { useAllActiveResources } from "@foundation/src/hooks/useResources";
import { useResourceTypes } from "@foundation/src/hooks/useResourceTypes";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";

/** The QR sticker codes one resource carries. */
export const useResourceScanCodes = (resourceId: string) =>
  useQuery({
    queryKey: qk.resources.scanCodes(resourceId),
    queryFn: () => getResourceScanCodes(resourceId),
    staleTime: STALE.OPERATIONAL,
  });

/** Resolves a scanned code. A mutation, not a query: every scan is a fresh, one-off lookup. */
export const useScanCodeLookup = () =>
  useMutation({
    mutationFn: lookupScanCode,
    meta: { errorMessage: "The scanned code could not be checked. Try again." },
  });

interface LinkScanCodeVariables {
  resourceId: string;
  code: string;
  /** Takes the code from the resource that carries it now. */
  move?: boolean;
}

/** Links a scanned code to a resource, or moves it there from another resource. */
export const useLinkResourceScanCode = () =>
  useMutation({
    mutationFn: ({ resourceId, code, move = false }: LinkScanCodeVariables) =>
      linkResourceScanCode(resourceId, code, move),
    meta: {
      successMessage: (_data: unknown, vars: unknown) =>
        (vars as LinkScanCodeVariables).move ? "QR code moved" : "QR code linked",
      errorMessage: "Failed to link QR code",
      // A move empties another resource's list, so refresh every list, not only this one.
      invalidates: [qk.resources.scanCodesAll()],
    },
  });

/**
 * Active resources a scanned, unknown code can be linked to: those whose type has QR
 * codes turned on.
 */
export const useScanLinkCandidates = (enabled: boolean) => {
  const { data: resources } = useAllActiveResources(enabled);
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
