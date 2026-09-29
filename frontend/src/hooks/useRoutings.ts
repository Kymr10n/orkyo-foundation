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
import { savedMessage, type SaveVariables } from "@foundation/src/hooks/mutation-utils";
import type {
  CreateRoutingRequest,
  InstantiateRoutingRequest,
  UpdateRoutingRequest,
} from "@foundation/src/types/routings";

export function useRoutings() {
  return useQuery({ queryKey: qk.routings(), queryFn: getRoutings });
}

/** Create (`id: null`) or update a routing — the edit dialog's save; it shows a failure inline. */
export function useSaveRouting() {
  return useMutation({
    mutationFn: (v: SaveVariables<CreateRoutingRequest, UpdateRoutingRequest>) =>
      v.id === null ? createRouting(v.data) : updateRouting(v.id, v.data),
    meta: {
      successMessage: savedMessage("Routing created", "Routing updated"),
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
