import { useMutation, useQuery } from "@tanstack/react-query";
import {
  createListRow,
  deleteListRow,
  ensureResourceListInstance,
  getListRows,
  getListInstance,
  getResourceListInstance,
  updateListRow,
  type ListRowRequest,
} from "@foundation/src/lib/api/lists-api";
import { qk } from "@foundation/src/lib/api/query-keys";

/**
 * The rows of one instance. Keyed by instance id, which is the only thing row CRUD needs — a
 * shared instance and a per-resource one are read and written the same way, so there is one data
 * path rather than an adapter per kind.
 */
export const useListRows = (instanceId: string | null) =>
  useQuery({
    queryKey: qk.lists.instanceRows(instanceId ?? "none"),
    queryFn: () => getListRows(instanceId!),
    // A per-resource list has no instance until its first write, and that is an ordinary state:
    // the caller renders an empty table rather than a spinner that never resolves.
    enabled: instanceId !== null,
  });

/**
 * One instance on its own — used when the caller holds an instance id and needs the definition
 * behind it, as a lookup field does to label the rows it offers.
 */
export const useListInstance = (instanceId: string | null) =>
  useQuery({
    queryKey: qk.lists.instance(instanceId ?? "none"),
    queryFn: () => getListInstance(instanceId!),
    enabled: instanceId !== null,
  });

export const useCreateListRow = (instanceId: string | null) =>
  useMutation({
    mutationFn: (request: ListRowRequest) => createListRow(instanceId!, request),
    meta: {
      successMessage: "Row added",
      errorMessage: "Failed to add row",
      invalidates: [qk.lists.instanceRows(instanceId ?? "none")],
    },
  });

export const useUpdateListRow = (instanceId: string | null) =>
  useMutation({
    mutationFn: ({ rowId, request }: { rowId: string; request: ListRowRequest }) =>
      updateListRow(instanceId!, rowId, request),
    meta: {
      successMessage: "Row updated",
      errorMessage: "Failed to update row",
      invalidates: [qk.lists.instanceRows(instanceId ?? "none")],
    },
  });

/**
 * Deletes a row. For a shared instance this also drops the row from every resource that had
 * selected it, so resource reads are invalidated alongside the rows themselves — `all()` and
 * `allFlat()` both, because the two live under separate roots on purpose and the resource lists
 * that show lookup labels read the first one.
 */
export const useDeleteListRow = (instanceId: string | null) =>
  useMutation({
    mutationFn: (rowId: string) => deleteListRow(instanceId!, rowId),
    meta: {
      successMessage: "Row removed",
      errorMessage: "Failed to remove row",
      invalidates: [
        qk.lists.instanceRows(instanceId ?? "none"),
        qk.resources.all(),
        qk.resources.allFlat(),
      ],
    },
  });

/**
 * Resolves the instance behind one resource's list field, and exposes `ensure()` for the caller
 * to invoke before its first write.
 *
 * The split matters: reading must not create anything, or merely opening a resource would leave a
 * trail of empty instances behind every list field on the form. Creation happens on the first row
 * the user actually adds.
 */
export const useResourceListInstance = (resourceId: string | null, fieldId: string) => {
  const query = useQuery({
    queryKey: qk.lists.resourceInstance(resourceId ?? "none", fieldId),
    queryFn: () => getResourceListInstance(resourceId!, fieldId),
    enabled: resourceId !== null,
  });

  const ensure = useMutation({
    mutationFn: () => ensureResourceListInstance(resourceId!, fieldId),
    meta: {
      // No success toast: this is plumbing the user did not ask for, and it fires on the way to
      // an action that has its own confirmation.
      errorMessage: "Failed to prepare the list",
      invalidates: [qk.lists.resourceInstance(resourceId ?? "none", fieldId)],
    },
  });

  return {
    instance: query.data ?? null,
    instanceId: query.data?.id ?? null,
    isLoading: query.isLoading,
    error: query.error,
    /** Returns the instance id, creating the instance on first use. Idempotent. */
    ensureInstanceId: async () => (query.data?.id ?? (await ensure.mutateAsync()).id),
  };
};
