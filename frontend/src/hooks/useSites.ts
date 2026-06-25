import { useMutation, useQuery } from "@tanstack/react-query";
import { createSite, deleteSite, getSites, updateSite } from "@foundation/src/lib/api/site-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";
import type { CreateSiteRequest, UpdateSiteRequest } from "@foundation/src/types/site";

const SITES_QUERY_KEY = ["sites"] as const;

// Deleting a site cascades to spaces and requests; over-invalidating on
// create/update is harmless and keeps the feedback declaration uniform.
const SITE_INVALIDATES = [SITES_QUERY_KEY, qk.spaces.all(), qk.requests.all()] as const;

export const useSites = () =>
  useQuery({
    queryKey: SITES_QUERY_KEY,
    queryFn: () => getSites(),
    staleTime: STALE.OPERATIONAL,
  });

export const useCreateSite = () =>
  useMutation({
    mutationFn: (data: CreateSiteRequest) => createSite(data),
    meta: {
      successMessage: "Site created",
      errorMessage: "Failed to create site",
      invalidates: SITE_INVALIDATES,
    },
  });

export const useUpdateSite = () =>
  useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateSiteRequest }) => updateSite(id, data),
    meta: {
      successMessage: "Site updated",
      errorMessage: "Failed to update site",
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
