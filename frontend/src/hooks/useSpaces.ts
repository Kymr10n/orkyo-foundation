import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { createSpace, deleteSpace, getSpaces, updateSpace } from "@foundation/src/lib/api/space-api";
import type { CreateSpaceRequest, SpaceGeometry, Space as SpaceType, UpdateSpaceRequest } from "@foundation/src/types/space";

// Query hook
export function useSpaces(siteId: string | null) {
  return useQuery({
    queryKey: ["spaces", siteId],
    queryFn: () => getSpaces(siteId!),
    enabled: !!siteId,
    staleTime: 5 * 60 * 1000,
    refetchOnWindowFocus: false,
  });
}

// Factory for space mutations with automatic cache invalidation
function useSpaceMutation(siteId: string) {
  const queryClient = useQueryClient();
  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ["spaces", siteId] });
    queryClient.invalidateQueries({ queryKey: ["requests"] });
  };
  return { queryClient, invalidate };
}

export function useCreateSpace(siteId: string) {
  const { invalidate } = useSpaceMutation(siteId);
  return useMutation({
    mutationFn: (data: CreateSpaceRequest) => createSpace(siteId, data),
    onSuccess: invalidate,
  });
}

export function useUpdateSpace(siteId: string) {
  const { invalidate } = useSpaceMutation(siteId);
  return useMutation({
    mutationFn: ({ resourceId, data }: { resourceId: string; data: UpdateSpaceRequest }) =>
      updateSpace(siteId, resourceId, data),
    onSuccess: invalidate,
  });
}

export function useDeleteSpace(siteId: string) {
  const { queryClient, invalidate } = useSpaceMutation(siteId);
  return useMutation({
    mutationFn: (resourceId: string) => deleteSpace(siteId, resourceId),
    onMutate: async (resourceId: string) => {
      // Cancel any in-flight refetches so they don't overwrite optimistic update
      await queryClient.cancelQueries({ queryKey: ["spaces", siteId] });
      // Snapshot current cache for rollback
      const previous = queryClient.getQueryData<SpaceType[]>(["spaces", siteId]);
      // Optimistically remove the space immediately
      queryClient.setQueryData<SpaceType[]>(["spaces", siteId], (old) =>
        old ? old.filter((s) => s.id !== resourceId) : []
      );
      return { previous };
    },
    onError: (_err, _resourceId, context) => {
      // Rollback to snapshot on failure
      if (context?.previous !== undefined) {
        queryClient.setQueryData(["spaces", siteId], context.previous);
      }
    },
    onSettled: invalidate,
  });
}

export function useMoveSpace(siteId: string) {
  const { invalidate } = useSpaceMutation(siteId);
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
    onSuccess: invalidate,
  });
}
