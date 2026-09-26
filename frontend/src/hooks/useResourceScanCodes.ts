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
import { useIsMultiSite, useSites } from "@foundation/src/hooks/useSites";
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

export interface ScanLinkType {
  key: string;
  displayName: string;
}

export interface ScanLinkCandidate {
  id: string;
  name: string;
  typeKey: string;
  typeName: string;
  /** Set on a multi-site tenant, so two resources with one name still read apart. */
  siteName?: string;
}

/**
 * What the "Link QR code" dialog can pick from: the types with QR codes turned on, and the
 * active resources of those types. The dialog narrows by type itself, so the two lists come
 * back separately rather than pre-flattened into options.
 */
export const useScanLinkCandidates = (enabled: boolean): { types: ScanLinkType[]; candidates: ScanLinkCandidate[] } => {
  const { data: resources } = useAllActiveResources(enabled);
  const { data: resourceTypes } = useResourceTypes();
  const { data: sites } = useSites();
  const isMultiSite = useIsMultiSite();

  const types = (resourceTypes ?? [])
    .filter((t) => t.isActive && t.scanCodesEnabled)
    .map((t) => ({ key: t.key, displayName: t.displayName }))
    .sort((a, b) => a.displayName.localeCompare(b.displayName));
  const typeName = new Map(types.map((t) => [t.key, t.displayName]));
  const siteName = new Map((sites ?? []).map((s) => [s.id, s.name]));

  const candidates = (resources ?? [])
    .filter((r) => typeName.has(r.resourceTypeKey))
    .map((r) => {
      const siteId = r.homeSiteId ?? r.currentSiteId;
      return {
        id: r.id,
        name: r.name,
        typeKey: r.resourceTypeKey,
        typeName: typeName.get(r.resourceTypeKey)!,
        siteName: isMultiSite && siteId ? siteName.get(siteId) : undefined,
      };
    });

  return { types, candidates };
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
