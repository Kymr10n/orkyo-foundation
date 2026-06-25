import { QueryClient, MutationCache } from '@tanstack/react-query';
import { toast } from 'sonner';

// Centralized mutation feedback. Instead of every dialog hand-rolling toast +
// cache invalidation in its own onSuccess/onError, a mutation declares its intent
// via `meta` and this cache fires the toast and invalidation once, in one place.
//
//   useMutation({
//     mutationFn: ...,
//     meta: { successMessage: 'Skills saved', invalidates: [['resource-capabilities', id]] },
//   })
//
// See docs/dialog-feedback.md and the `mutationMeta` augmentation below.
declare module '@tanstack/react-query' {
  interface Register {
    mutationMeta: {
      /** Toast fired on success. Omit to stay silent. */
      successMessage?: string;
      /** Title for the error toast. Defaults to "Something went wrong". */
      errorMessage?: string;
      /** Query keys invalidated on success, prefix-style (exact: false). */
      invalidates?: readonly (readonly unknown[])[];
    };
  }
}

/**
 * Builds the MutationCache that drives meta-based feedback. Exported so tests can
 * construct a client with the identical behaviour (passing the mocked toast),
 * keeping prod and test on one implementation.
 */
export function createFeedbackMutationCache(
  getClient: () => QueryClient,
  toastImpl: Pick<typeof toast, 'success' | 'error'> = toast,
) {
  return new MutationCache({
    onSuccess: (_data, _vars, _ctx, mutation) => {
      const meta = mutation.meta;
      meta?.invalidates?.forEach((queryKey) => {
        getClient().invalidateQueries({ queryKey, exact: false });
      });
      if (meta?.successMessage) toastImpl.success(meta.successMessage);
    },
    onError: (err, _vars, _ctx, mutation) => {
      const meta = mutation.meta;
      // Opt-in guard: only mutations that declared feedback get a global error
      // toast, so un-migrated/legacy mutations are untouched (no double-toast).
      if (meta?.successMessage || meta?.errorMessage) {
        toastImpl.error(meta.errorMessage ?? 'Something went wrong', {
          description: err instanceof Error ? err.message : undefined,
        });
      }
    },
  });
}

/**
 * Named query-freshness tiers. The global default below is the "standard" 5-minute
 * staleTime; hooks that match it should omit `staleTime` and inherit. These constants
 * exist so the genuinely-intentional deviations read by intent rather than a bare
 * magic number, and so a tier's value is defined once.
 */
export const STALE = {
  /** Near-real-time operational data (conflicts registry, quotas). */
  REALTIME: 30 * 1000,
  /** Settings/feeds that change on the minute scale (scheduling settings, availability events). */
  OPERATIONAL: 60 * 1000,
  /** Standard tier — the global default; hooks at this freshness omit `staleTime`. */
  STANDARD: 5 * 60 * 1000,
  /** Slow-moving analytics (insights dashboards). */
  ANALYTICS: 15 * 60 * 1000,
} as const;

export const queryClient: QueryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: STALE.STANDARD,
      // Most hooks opt out of focus refetching; make it the global default so they
      // stop repeating it. The few that genuinely want focus refetch can override.
      refetchOnWindowFocus: false,
      retry: 1,
    },
  },
  // The cache callbacks need the client to invalidate; a lazy getter resolves the
  // `queryClient` binding at run-time (callbacks never fire during construction).
  mutationCache: createFeedbackMutationCache(() => queryClient),
});
