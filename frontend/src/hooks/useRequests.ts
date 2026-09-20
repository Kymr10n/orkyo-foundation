import { useCallback } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createRequest,
  deleteRequest,
  deleteRequestSubtree,
  getRequests,
  moveRequest,
  updateRequest,
} from "@foundation/src/lib/api/request-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import {
  REQUEST_DERIVED_QUERY_KEYS,
  invalidateRequestData,
} from "@foundation/src/lib/core/invalidate-request-data";
import { buildCreatePayload, buildUpdatePayload } from "@foundation/src/lib/utils/utils";
import type { Request, RequestFormData } from "@foundation/src/types/requests";

/**
 * The request list for a site. Site-neutral requests are kept in by the backend, so the
 * picker scopes the list without hiding them.
 *
 * Lives in the query cache under the shared `requests` prefix, so the mutations'
 * `meta.invalidates` refreshes it after any change — no manual re-fetch bookkeeping.
 */
export function useRequests(siteId: string | null) {
  return useQuery({
    queryKey: qk.requests.list(siteId),
    queryFn: () => getRequests(true, siteId ?? undefined),
  });
}

/**
 * Invalidate every request-derived namespace. Exposed as a callback so components never
 * hold the query client themselves.
 */
export function useInvalidateRequestData(): () => void {
  const queryClient = useQueryClient();
  return useCallback(() => invalidateRequestData(queryClient), [queryClient]);
}

/** Reparent a request under `targetId`. Resolves to the new parent id. */
export function useMoveRequestToParent(handlers: {
  onSuccess: (targetId: string) => void;
  onError: (error: unknown) => void;
}) {
  return useMutation({
    mutationFn: async ({
      draggedId,
      targetId,
      sortOrder,
    }: {
      draggedId: string;
      targetId: string;
      sortOrder: number;
    }) => {
      await moveRequest(draggedId, { newParentRequestId: targetId, sortOrder });
      return targetId;
    },
    meta: { errorMessage: "Failed to move request", invalidates: REQUEST_DERIVED_QUERY_KEYS },
    onSuccess: (targetId) => handlers.onSuccess(targetId),
    onError: (err) => handlers.onError(err),
  });
}

/** Delete a request — subtree delete when it has descendants. */
export function useDeleteRequest(handlers: {
  onSuccess: (deleted: { request: Request; descendantIds: string[] }) => void;
  onError: (error: unknown) => void;
}) {
  return useMutation({
    mutationFn: async ({
      request,
      descendantIds,
    }: {
      request: Request;
      descendantIds: string[];
    }) => {
      // Use subtree delete if the request has descendants
      if (descendantIds.length > 0) await deleteRequestSubtree(request.id);
      else await deleteRequest(request.id);
      return { request, descendantIds };
    },
    meta: {
      successMessage: "Request deleted",
      errorMessage: "Failed to delete request",
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
    onSuccess: (deleted) => handlers.onSuccess(deleted),
    onError: (err) => handlers.onError(err),
  });
}

/** Create or update a request from the dialog's form data. */
export function useSaveRequest(handlers: {
  onSuccess: () => void;
  onError: (error: unknown) => void;
}) {
  return useMutation({
    mutationFn: ({ data, editing }: { data: RequestFormData; editing: Request | null }) =>
      editing
        ? updateRequest(editing.id, buildUpdatePayload(data, editing.planningMode, editing.siteId))
        : createRequest(buildCreatePayload(data)),
    meta: {
      successMessage: (_saved, variables) =>
        (variables as { editing: Request | null }).editing ? "Request updated" : "Request created",
      suppressErrorToast: true,
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
    onSuccess: () => handlers.onSuccess(),
    onError: (err) => handlers.onError(err),
  });
}
