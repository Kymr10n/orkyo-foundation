import { useMutation, useQuery } from "@tanstack/react-query";
import {
  createListColumn,
  createListDefinition,
  deleteListColumn,
  deleteListDefinition,
  getListDefinition,
  getListDefinitions,
  updateListColumn,
  updateListDefinition,
  type CreateListColumnRequest,
  type CreateListDefinitionRequest,
  type UpdateListColumnRequest,
  type UpdateListDefinitionRequest,
} from "@foundation/src/lib/api/lists-api";
import { qk } from "@foundation/src/lib/api/query-keys";

/**
 * A column change reshapes every row of every instance built from the definition, so the whole
 * list namespace is invalidated rather than one key — rows read elsewhere are stale the moment a
 * column is added, renamed or removed.
 */
const LIST_INVALIDATES = [qk.lists.all()] as const;

export const useListDefinitions = (includeInactive = false) =>
  useQuery({
    queryKey: [...qk.lists.definitions(), includeInactive] as const,
    queryFn: () => getListDefinitions(includeInactive),
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
