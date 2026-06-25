import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { createSpace, deleteSpace, getSpaces, updateSpace } from "@foundation/src/lib/api/space-api";
import type { CreateSpaceRequest, SpaceGeometry, Space as SpaceType, UpdateSpaceRequest } from "@foundation/src/types/space";
import { qk } from "@foundation/src/lib/api/query-keys";
import { errorMessage } from "./mutation-utils";

// Query hook
export function useSpaces(siteId: string | null) {
  return useQuery({
    queryKey: qk.spaces.list(siteId),
    queryFn: () => getSpaces(siteId!),
    enabled: !!siteId,
  });
}

// Space mutations invalidate the site's spaces and the request feed (assignments).
const spaceInvalidates = (siteId: string) =>
  [qk.spaces.list(siteId), qk.requests.all()] as const;

export function useCreateSpace(siteId: string) {
  return useMutation({
    mutationFn: (data: CreateSpaceRequest) => createSpace(siteId, data),
    meta: {
      successMessage: "Space created",
      errorMessage: "Failed to create space",
      invalidates: spaceInvalidates(siteId),
    },
  });
}

export function useUpdateSpace(siteId: string) {
  return useMutation({
    mutationFn: ({ resourceId, data }: { resourceId: string; data: UpdateSpaceRequest }) =>
      updateSpace(siteId, resourceId, data),
    meta: {
      successMessage: "Space updated",
      errorMessage: "Failed to update space",
      invalidates: spaceInvalidates(siteId),
    },
  });
}

export function useDeleteSpace(siteId: string) {
  // Optimistic: kept hand-rolled because the meta convention can't express onMutate
  // rollback. Invalidation is fired manually in onSettled to mirror the meta hooks.
  const queryClient = useQueryClient();
  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: qk.spaces.list(siteId) });
    queryClient.invalidateQueries({ queryKey: qk.requests.all() });
  };
  return useMutation({
    mutationFn: (resourceId: string) => deleteSpace(siteId, resourceId),
    onMutate: async (resourceId: string) => {
      // Cancel any in-flight refetches so they don't overwrite optimistic update
      await queryClient.cancelQueries({ queryKey: qk.spaces.list(siteId) });
      // Snapshot current cache for rollback
      const previous = queryClient.getQueryData<SpaceType[]>(qk.spaces.list(siteId));
      // Optimistically remove the space immediately
      queryClient.setQueryData<SpaceType[]>(qk.spaces.list(siteId), (old) =>
        old ? old.filter((s) => s.id !== resourceId) : []
      );
      return { previous };
    },
    onError: (err, _resourceId, context) => {
      // Rollback to snapshot on failure
      if (context?.previous !== undefined) {
        queryClient.setQueryData(qk.spaces.list(siteId), context.previous);
      }
      toast.error("Failed to delete space", { description: errorMessage(err) });
    },
    onSuccess: () => {
      toast.success("Space deleted");
    },
    onSettled: invalidate,
  });
}

export function useMoveSpace(siteId: string) {
  return useMutation({
    mutationFn: ({ resourceId, space, newGeometry }: {
      resourceId: string;
      space: SpaceType;
      newGeometry: SpaceGeometry;
    }) => updateSpace(siteId, resourceId, {
      name: space.name,
      code: space.code,
      description: space.description,
      isPhysical: space.isPhysical,
      geometry: newGeometry,
    }),
    // Move/resize is a visible drag — silent on success, so no successMessage. The
    // error toast still routes through the meta convention (errorMessage opts in).
    meta: {
      errorMessage: "Failed to move space",
      invalidates: spaceInvalidates(siteId),
    },
  });
}
