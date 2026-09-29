/**
 * Shared test utilities for React Query hooks and components
 */
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import { type ReactElement, type ReactNode } from "react";
import { MemoryRouter } from "react-router";
import { vi } from "vitest";
import { createFeedbackMutationCache } from "@foundation/src/lib/core/query-client";
import { TooltipProvider } from "@foundation/src/components/ui/tooltip";

export interface TestQueryClientOptions {
  /**
   * Wire the same meta-driven feedback MutationCache as production
   * (`createFeedbackMutationCache`). Use this for components whose mutations declare
   * `meta.successMessage`/`invalidates` so the central toast + invalidation fire in tests
   * exactly as they do at runtime. `toast` is the spy double `src/test/setup.ts` installs
   * for `sonner`. Note the cache invalidates prefix-style: assert
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
  // TooltipProvider stands in for the one TenantApp/ApexGateway mount at the app root.
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <TooltipProvider>{children}</TooltipProvider>
    </QueryClientProvider>
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

export interface RenderWithQueryOptions extends TestQueryClientOptions {
  /** Wrap the UI in a MemoryRouter; a path sets its initial entry. */
  router?: boolean | string;
}

/**
 * Render `ui` inside a fresh test QueryClient (see `createTestQueryClient`), optionally under a
 * MemoryRouter. Returns the Testing Library result plus the client and its `invalidateQueries`
 * spy; `rerender` keeps the same client.
 */
export function renderWithQuery(
  ui: ReactElement,
  { router, ...clientOptions }: RenderWithQueryOptions = {},
) {
  const { queryClient, spy, wrapper: QueryWrapper } = createTestQueryClient(clientOptions);
  const initialEntries = typeof router === "string" ? [router] : undefined;
  const wrapper = router
    ? ({ children }: { children: ReactNode }) => (
        <QueryWrapper>
          <MemoryRouter initialEntries={initialEntries}>
            {children}
          </MemoryRouter>
        </QueryWrapper>
      )
    : QueryWrapper;
  return { ...render(ui, { wrapper }), queryClient, invalidateSpy: spy };
}
