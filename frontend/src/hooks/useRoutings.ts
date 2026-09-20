import { useMutation, useQuery } from "@tanstack/react-query";
import {
  createRouting,
  deleteRouting,
  getRoutings,
  instantiateRouting,
  updateRouting,
} from "@foundation/src/lib/api/routing-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { REQUEST_DERIVED_QUERY_KEYS } from "@foundation/src/lib/core/invalidate-request-data";
import type {
  CreateRoutingRequest,
  InstantiateRoutingRequest,
  UpdateRoutingRequest,
} from "@foundation/src/types/routings";

export function useRoutings() {
  return useQuery({ queryKey: qk.routings(), queryFn: getRoutings });
}

export function useCreateRouting() {
  return useMutation({
    mutationFn: (request: CreateRoutingRequest) => createRouting(request),
    meta: {
      successMessage: "Routing created",
      suppressErrorToast: true,
      invalidates: [qk.routings()],
    },
  });
}

export function useUpdateRouting() {
  return useMutation({
    mutationFn: ({ id, request }: { id: string; request: UpdateRoutingRequest }) =>
      updateRouting(id, request),
    meta: {
      successMessage: "Routing updated",
      suppressErrorToast: true,
      invalidates: [qk.routings()],
    },
  });
}

export function useDeleteRouting() {
  return useMutation({
    mutationFn: (id: string) => deleteRouting(id),
    meta: {
      successMessage: "Routing deleted",
      errorMessage: "Failed to delete routing",
      invalidates: [qk.routings()],
    },
  });
}

/** A work order lands in the request tree, so everything derived from requests refreshes. */
export function useInstantiateRouting() {
  return useMutation({
    mutationFn: ({ id, request }: { id: string; request: InstantiateRoutingRequest }) =>
      instantiateRouting(id, request),
    meta: {
      successMessage: "Work order created",
      suppressErrorToast: true,
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
  });
}
