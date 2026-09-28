import { useMutation, useQuery } from "@tanstack/react-query";
import { createSite, deleteSite, getSites, updateSite } from "@foundation/src/lib/api/site-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";
import type { CreateSiteRequest, UpdateSiteRequest } from "@foundation/src/types/site";
import { savedMessage, type SaveVariables } from "@foundation/src/hooks/mutation-utils";

// Deleting a site cascades to spaces and requests; over-invalidating on
// create/update is harmless and keeps the feedback declaration uniform.
const SITE_INVALIDATES = [qk.sites.list(), qk.resources.all(), qk.requests.all()] as const;

export const useSites = () =>
  useQuery({
    queryKey: qk.sites.list(),
    queryFn: () => getSites(),
    staleTime: STALE.OPERATIONAL,
  });

export const useCreateSite = () =>
  useMutation({
    mutationFn: (data: CreateSiteRequest) => createSite(data),
    meta: {
      successMessage: "Site created",
      suppressErrorToast: true,
      invalidates: SITE_INVALIDATES,
    },
  });

/** Create (`id: null`) or update a site — the edit dialog's save; it shows a failure inline. */
export const useSaveSite = () =>
  useMutation({
    mutationFn: (v: SaveVariables<CreateSiteRequest, UpdateSiteRequest>) =>
      v.id === null ? createSite(v.data) : updateSite(v.id, v.data),
    meta: {
      successMessage: savedMessage("Site created", "Site updated"),
      suppressErrorToast: true,
      invalidates: SITE_INVALIDATES,
    },
  });

export const useDeleteSite = () =>
  useMutation({
    mutationFn: (id: string) => deleteSite(id),
    meta: {
      successMessage: "Site deleted",
      errorMessage: "Failed to delete site",
      invalidates: SITE_INVALIDATES,
    },
  });

/**
 * Whether the tenant has more than one site. Single source of truth for the
 * site-model's progressive disclosure: when false, all site UI (request Site
 * picker, people home/current/cross-site fields, candidate site badges) is
 * hidden so single-site / free-tier tenants never see the concept.
 */
export const useIsMultiSite = (): boolean => {
  const { data: sites } = useSites();
  return (sites?.length ?? 0) > 1;
};
