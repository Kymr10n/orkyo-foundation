import { useCallback, useState } from "react";
import { hashKey, useQueryClient, type QueryKey } from "@tanstack/react-query";

/**
 * A callback that invalidates every given key (prefix match) and resolves once the active
 * queries under them have refetched. The callback keeps its identity while the keys keep their
 * content, so it is safe in effect and callback dependencies although callers pass new arrays
 * on every render.
 */
export function useInvalidateKeys(...queryKeys: QueryKey[]): () => Promise<void> {
  const queryClient = useQueryClient();
  const hash = hashKey(queryKeys);
  // Render-phase sync, not an effect (see useEntityFormDialog.ts): adopt the new keys only
  // when their content changed.
  const [current, setCurrent] = useState({ hash, queryKeys });
  if (current.hash !== hash) setCurrent({ hash, queryKeys });

  return useCallback(async () => {
    await Promise.all(
      current.queryKeys.map((queryKey) => queryClient.invalidateQueries({ queryKey })),
    );
  }, [queryClient, current]);
}
