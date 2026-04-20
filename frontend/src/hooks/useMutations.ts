import { useMutation, useQueryClient, useQuery as useTanstackQuery, type UseQueryOptions } from "@tanstack/react-query";

/**
 * Generic factory for creating CRUD mutation hooks with automatic cache invalidation
 */

interface MutationConfig<TData, TCreate, TUpdate, TParams> {
  queryKey: (params?: TParams) => readonly unknown[];
  queryFn: (params?: TParams) => Promise<TData[]>;
  createFn?: (data: TCreate, params?: TParams) => Promise<TData>;
  updateFn?: (id: string, data: TUpdate, params?: TParams) => Promise<TData>;
  deleteFn?: (id: string, params?: TParams) => Promise<void>;
  invalidateKeys?: (params?: TParams) => unknown[][];
}

export function createCrudHooks<TData, TCreate = Partial<TData>, TUpdate = Partial<TData>, TParams = undefined>(
  config: MutationConfig<TData, TCreate, TUpdate, TParams>
) {
  const invalidateAll = (queryClient: ReturnType<typeof useQueryClient>, params?: TParams) => {
    const keysToInvalidate = [
      config.queryKey(params),
      ...(config.invalidateKeys?.(params) || []),
    ];
    keysToInvalidate.forEach(key => {
      queryClient.invalidateQueries({ queryKey: key });
    });
  };

  // Query hook
  const useQuery = (params?: TParams, options?: Partial<UseQueryOptions<TData[]>>) => {
    return useTanstackQuery({
      queryKey: config.queryKey(params),
      queryFn: () => config.queryFn(params),
      enabled: params !== null && params !== undefined,
      staleTime: 60 * 1000,
      ...options,
    });
  };

  // Create mutation
  const useCreate = (params?: TParams) => {
    const queryClient = useQueryClient();

    return useMutation({
      mutationFn: (data: TCreate) => config.createFn!(data, params),
      onSuccess: () => invalidateAll(queryClient, params),
    });
  };

  // Update mutation
  const useUpdate = (params?: TParams) => {
    const queryClient = useQueryClient();

    return useMutation({
      mutationFn: ({ id, data }: { id: string; data: TUpdate }) =>
        config.updateFn!(id, data, params),
      onSuccess: () => invalidateAll(queryClient, params),
    });
  };

  // Delete mutation
  const useDelete = (params?: TParams) => {
    const queryClient = useQueryClient();

    return useMutation({
      mutationFn: (id: string) => config.deleteFn!(id, params),
      onSuccess: () => invalidateAll(queryClient, params),
    });
  };

  return {
    useQuery,
    useCreate,
    useUpdate,
    useDelete,
  };
}
