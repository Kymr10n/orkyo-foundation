import { useMutation, useQueries, useQuery } from "@tanstack/react-query";
import {
  createListColumn,
  createListDefinition,
  createSharedListInstance,
  deleteListColumn,
  deleteListDefinition,
  deleteSharedListInstance,
  getListDefinition,
  getListDefinitions,
  getSharedListInstances,
  updateListColumn,
  updateListDefinition,
  updateSharedListInstance,
  type CreateListColumnRequest,
  type CreateListDefinitionRequest,
  type ListDefinitionScope,
  type UpdateListColumnRequest,
  type ListInstanceRequest,
  type UpdateListDefinitionRequest,
} from "@foundation/src/lib/api/lists-api";
import { qk } from "@foundation/src/lib/api/query-keys";

/**
 * A column change reshapes every row of every instance built from the definition, so the whole
 * list namespace is invalidated rather than one key — rows read elsewhere are stale the moment a
 * column is added, renamed or removed.
 */
const LIST_INVALIDATES = [qk.lists.all()] as const;

export const useListDefinitions = (includeInactive = false, scope?: ListDefinitionScope) =>
  useQuery({
    queryKey: [...qk.lists.definitions(), includeInactive, scope ?? null] as const,
    queryFn: () => getListDefinitions(includeInactive, scope),
  });

/** One definition with its columns. Null id disables the query — nothing to fetch yet. */
export const useListDefinition = (definitionId: string | null) =>
  useQuery({
    queryKey: qk.lists.definition(definitionId ?? "none"),
    queryFn: () => getListDefinition(definitionId!),
    enabled: definitionId !== null,
  });

export const useCreateListDefinition = () =>
  useMutation({
    mutationFn: (request: CreateListDefinitionRequest) => createListDefinition(request),
    meta: {
      successMessage: "List definition created",
      errorMessage: "Failed to create list definition",
      invalidates: LIST_INVALIDATES,
    },
  });

export const useUpdateListDefinition = () =>
  useMutation({
    mutationFn: ({
      definitionId,
      request,
    }: {
      definitionId: string;
      request: UpdateListDefinitionRequest;
    }) => updateListDefinition(definitionId, request),
    meta: {
      successMessage: "List definition updated",
      errorMessage: "Failed to update list definition",
      invalidates: LIST_INVALIDATES,
    },
  });

/** Rejected with a 409 while any field or shared instance still references the definition. */
export const useDeleteListDefinition = () =>
  useMutation({
    mutationFn: (definitionId: string) => deleteListDefinition(definitionId),
    meta: {
      successMessage: "List definition removed",
      errorMessage: "Failed to remove list definition",
      invalidates: LIST_INVALIDATES,
    },
  });

export const useCreateListColumn = (definitionId: string) =>
  useMutation({
    mutationFn: (request: CreateListColumnRequest) => createListColumn(definitionId, request),
    meta: {
      successMessage: "Column added",
      errorMessage: "Failed to add column",
      invalidates: LIST_INVALIDATES,
    },
  });

export const useUpdateListColumn = (definitionId: string) =>
  useMutation({
    mutationFn: ({ columnId, request }: { columnId: string; request: UpdateListColumnRequest }) =>
      updateListColumn(definitionId, columnId, request),
    meta: {
      successMessage: "Column updated",
      errorMessage: "Failed to update column",
      invalidates: LIST_INVALIDATES,
    },
  });

/** Deletes the column and discards the cells rows hold for it. */
export const useDeleteListColumn = (definitionId: string) =>
  useMutation({
    mutationFn: (columnId: string) => deleteListColumn(definitionId, columnId),
    meta: {
      successMessage: "Column removed",
      errorMessage: "Failed to remove column",
      invalidates: LIST_INVALIDATES,
    },
  });

// ── shared instances ────────────────────────────────────────────────────────

/** The named instances of one definition. Null id disables the query. */
export const useSharedListInstances = (definitionId: string | null) =>
  useQuery({
    queryKey: qk.lists.sharedInstances(definitionId ?? "none"),
    queryFn: () => getSharedListInstances(definitionId!),
    enabled: definitionId !== null,
  });

export const useCreateSharedListInstance = (definitionId: string) =>
  useMutation({
    mutationFn: (request: ListInstanceRequest) => createSharedListInstance(definitionId, request),
    meta: {
      successMessage: "Shared list created",
      errorMessage: "Failed to create shared list",
      invalidates: LIST_INVALIDATES,
    },
  });

export const useUpdateSharedListInstance = (definitionId: string) =>
  useMutation({
    mutationFn: ({ instanceId, request }: { instanceId: string; request: ListInstanceRequest }) =>
      updateSharedListInstance(definitionId, instanceId, request),
    meta: {
      successMessage: "Shared list renamed",
      errorMessage: "Failed to rename shared list",
      invalidates: LIST_INVALIDATES,
    },
  });

/** Rejected with a 409 while a lookup field still points at the instance. */
export const useDeleteSharedListInstance = (definitionId: string) =>
  useMutation({
    mutationFn: (instanceId: string) => deleteSharedListInstance(definitionId, instanceId),
    meta: {
      successMessage: "Shared list removed",
      errorMessage: "Failed to remove shared list",
      invalidates: LIST_INVALIDATES,
    },
  });

/**
 * Every shared instance the tenant has, paired with the definition it came from.
 *
 * There is no endpoint that returns this in one call, and adding one for a picker that runs on a
 * handful of definitions would be a server change to save a few parallel reads. React Query fans
 * the per-definition calls out and caches them under the same keys the instances dialog uses, so
 * opening that dialog afterwards costs nothing.
 */
export const useAllSharedListInstances = () => {
  const { data: definitions = [] } = useListDefinitions();

  const results = useQueries({
    queries: definitions.map((definition) => ({
      queryKey: qk.lists.sharedInstances(definition.id),
      queryFn: () => getSharedListInstances(definition.id),
    })),
  });

  return definitions.flatMap((definition, i) =>
    (results[i]?.data ?? []).map((instance) => ({ definitionName: definition.name, instance })),
  );
};
