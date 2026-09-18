/**
 * Shared test utilities for React Query hooks
 */
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { type ReactNode } from "react";
import { vi } from "vitest";
import { createFeedbackMutationCache } from "@foundation/src/lib/core/query-client";

export interface TestQueryClientOptions {
  /**
   * Wire the same meta-driven feedback MutationCache as production
   * (`createFeedbackMutationCache`). Use this for components whose mutations declare
   * `meta.successMessage`/`invalidates` so the central toast + invalidation fire in tests
   * exactly as they do at runtime. `toast` is resolved from the (mocked) `sonner` module
   * the test sets up. Note the cache invalidates prefix-style: assert
   * `{ queryKey, exact: false }`.
   */
  feedback?: boolean;
}

/**
 * Create a test QueryClient (retry disabled) with an `invalidateQueries` spy and a wrapper
 * component. Use this when the test needs the client instance or asserts which cache keys
 * a mutation invalidates; otherwise `createTestQueryWrapper` is enough.
 */
export function createTestQueryClient({ feedback = false }: TestQueryClientOptions = {}) {
  const queryClient: QueryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
    ...(feedback && { mutationCache: createFeedbackMutationCache(() => queryClient) }),
  });
  const spy = vi.spyOn(queryClient, "invalidateQueries");
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  return { queryClient, spy, wrapper };
}

/**
 * Create a test QueryClient wrapper with retry disabled.
 * Useful for testing React Query hooks in isolation.
 */
export function createTestQueryWrapper(options: TestQueryClientOptions = {}) {
  return createTestQueryClient(options).wrapper;
}
