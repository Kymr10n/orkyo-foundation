import { useCallback, useEffect, useState } from "react";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  deleteResourceGroup,
  getResourceGroups,
  getResourceGroupMembers,
  setResourceGroupMembers,
  type ResourceGroupInfo,
} from "@foundation/src/lib/api/resource-groups-api";
import { getResources, type ResourceInfo } from "@foundation/src/lib/api/resources-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { logger } from "@foundation/src/lib/core/logger";
import { STALE } from "@foundation/src/lib/core/query-client";

/** Groups of one resource type (person teams, space groups, …). */
export const useResourceGroups = (resourceTypeKey: string) =>
  useQuery({
    queryKey: qk.resourceGroups.byType(resourceTypeKey),
    queryFn: () => getResourceGroups(resourceTypeKey),
  });

/**
 * Groups for several resource types at once, merged. One query per type key, so each still
 * shares its cache entry with the per-type group pages.
 *
 * `combine` rather than a memo over the results: useQueries hands back a fresh array every
 * render, and the alternative was a hand-rolled key over `dataUpdatedAt` that needed a lint
 * suppression and went stale whenever two queries settled in the same millisecond. combine's
 * result is identity-stable through replaceEqualDeep, which is what the callers' memos want.
 */
export const useResourceGroupsOfTypes = (resourceTypeKeys: readonly string[]) =>
  useQueries({
    queries: resourceTypeKeys.map((key) => ({
      queryKey: qk.resourceGroups.byType(key),
      queryFn: () => getResourceGroups(key),
    })),
    combine: (results) => ({
      groups: results.flatMap((r) => r.data ?? []) as ResourceGroupInfo[],
      groupsLoading: results.some((r) => r.isLoading),
    }),
  });

/**
 * Members per group — one query per group. Same pattern the resource lists use for per-resource
 * profile fetching. Acceptable at expected scale (tens).
 */
export const useResourceGroupMemberQueries = (groups: readonly ResourceGroupInfo[]) =>
  useQueries({
    queries: groups.map((g) => ({
      queryKey: qk.resourceGroups.members(g.id),
      queryFn: () => getResourceGroupMembers(g.id),
      staleTime: STALE.OPERATIONAL,
    })),
  });

export const useDeleteResourceGroup = (resourceTypeKey: string, entityLabel: string) =>
  useMutation({
    mutationFn: deleteResourceGroup,
    meta: {
      successMessage: `${entityLabel} deleted`,
      errorMessage: `Failed to delete ${entityLabel.toLowerCase()}`,
      invalidates: [qk.resourceGroups.byType(resourceTypeKey)],
    },
  });

/** Refresh one type's group list after a dialog saved through its own API call. */
export const useInvalidateResourceGroups = (resourceTypeKey: string) => {
  const queryClient = useQueryClient();
  return useCallback(() => {
    queryClient.invalidateQueries({ queryKey: qk.resourceGroups.byType(resourceTypeKey) });
  }, [queryClient, resourceTypeKey]);
};

export const useSetResourceGroupMembers = (groupId: string, resourceTypeKey: string) =>
  useMutation({
    mutationFn: (resourceIds: string[]) => setResourceGroupMembers(groupId, resourceIds),
    meta: {
      successMessage: "Members updated",
      suppressErrorToast: true,
      invalidates: [qk.resourceGroups.byType(resourceTypeKey)],
    },
  });

export interface ResourceGroupMembershipRoster {
  allResources: ResourceInfo[];
  selectedResourceIds: Set<string>;
  setSelectedResourceIds: (ids: Set<string>) => void;
  /** resourceId → the OTHER group it currently belongs to (1:1 rule). Empty unless singleGroup. */
  otherGroupByResource: Map<string, string>;
  isLoading: boolean;
  error: string | null;
  setError: (error: string | null) => void;
}

/**
 * The roster the members editor works over: this type's active resources, who is already in the
 * group, and — for single-group types — which other group each candidate sits in today.
 */
export function useResourceGroupMembershipRoster(
  open: boolean,
  groupId: string,
  resourceTypeKey: string,
  singleGroup: boolean,
): ResourceGroupMembershipRoster {
  const [allResources, setAllResources] = useState<ResourceInfo[]>([]);
  const [selectedResourceIds, setSelectedResourceIds] = useState<Set<string>>(new Set());
  const [otherGroupByResource, setOtherGroupByResource] = useState<Map<string, string>>(new Map());
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    const load = async () => {
      setIsLoading(true);
      setError(null);
      try {
        const [resourcesRes, membersRes] = await Promise.all([
          getResources({ resourceTypeKey, isActive: true }),
          getResourceGroupMembers(groupId),
        ]);
        if (cancelled) return;
        setAllResources(resourcesRes.items);
        setSelectedResourceIds(new Set(membersRes.members.map((m) => m.id)));

        // Single-group types are 1:1 with groups: map each resource already in a *different*
        // group so we can warn before moving it. Group count is small, so per-group fetch is cheap.
        if (singleGroup) {
          const groups = await getResourceGroups(resourceTypeKey);
          const others = groups.filter((g) => g.id !== groupId);
          const memberLists = await Promise.all(others.map((g) => getResourceGroupMembers(g.id)));
          if (cancelled) return;
          const map = new Map<string, string>();
          others.forEach((g, i) => {
            for (const m of memberLists[i].members) map.set(m.id, g.name);
          });
          setOtherGroupByResource(map);
        } else {
          setOtherGroupByResource(new Map());
        }
      } catch (err) {
        if (cancelled) return;
        logger.error("Failed to load resources / group members:", err);
        setError(err instanceof Error ? err.message : "Failed to load");
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    };
    load();
    return () => { cancelled = true; };
  }, [open, groupId, resourceTypeKey, singleGroup]);

  return {
    allResources,
    selectedResourceIds,
    setSelectedResourceIds,
    otherGroupByResource,
    isLoading,
    error,
    setError,
  };
}
