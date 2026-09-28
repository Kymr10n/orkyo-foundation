import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { globalSearch } from "@foundation/src/lib/api/search-api";
import { logger } from "@foundation/src/lib/core/logger";

/**
 * The command palette's search, for an already-debounced term. An empty term runs nothing. The
 * last answer stays on screen while the next one loads, so the list does not blink per keystroke.
 */
export const useGlobalSearch = (term: string, siteId: string | null) =>
  useQuery({
    queryKey: ["global-search", term, siteId] as const,
    queryFn: () =>
      globalSearch({ query: term, siteId: siteId ?? undefined, limit: 20 }).catch((err: unknown) => {
        logger.error("Search failed:", err);
        throw err;
      }),
    enabled: term.length > 0,
    placeholderData: keepPreviousData,
    retry: false,
  });
