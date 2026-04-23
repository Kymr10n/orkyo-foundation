/**
 * Shared test utilities for React Query hooks
 */
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { type ReactNode } from "react";
import { vi } from "vitest";

/**
 * Create a test QueryClient wrapper with retry disabled.
 * Useful for testing React Query hooks in isolation.
 */
export function createTestQueryWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}

/**
 * Create a QueryClient for tests (without wrapper).
 * Useful when you need the client instance directly.
 */
function createTestQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
}

/**
 * Create a QueryClient with an invalidateQueries spy and a wrapper component.
 * Useful for testing that mutations invalidate the correct cache keys.
 */
export function createTestQueryClientWithSpy() {
  const queryClient = createTestQueryClient();
  const spy = vi.spyOn(queryClient, "invalidateQueries");
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  return { queryClient, spy, wrapper };
}
