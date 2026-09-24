import { useEffect, useEffectEvent, useRef } from "react";
import { useSearchParams } from "react-router";

interface UseEditQueryParamOptions<T> {
  /** Skip while the list is still loading; defaults to true (ready). */
  ready?: boolean;
  /** How to read an item's id; defaults to `item.id`. */
  getId?: (item: T) => string;
  /**
   * Fetch the item when the loaded list does not hold it. A list can be scoped — by site, by
   * filter, by page — while a deep link is not, so "not in the list" does not mean "not a real
   * id". Without this the link is silently dropped and the reader is left on the list they were
   * trying to leave. Return null when the id is genuinely not a thing.
   */
  resolveMissing?: (id: string) => Promise<T | null>;
  /** Told when an id resolves to nothing, so the page can say so rather than do nothing. */
  onMissing?: (id: string) => void;
}

/**
 * Opens an item's detail dialog when the page is reached with `?edit=<id>` —
 * the convention the global search (CommandPalette) uses to deep-link a result.
 *
 * Once `items` (loaded) contains a match for the `edit` id, calls `onOpen(item)`
 * once and strips only the `edit` param (preserving any others). `onOpen` may be
 * an inline callback — it is held in a ref so it never retriggers the effect.
 */
export function useEditQueryParam<T>(
  items: readonly T[] | undefined,
  onOpen: (item: T) => void,
  options?: UseEditQueryParamOptions<T>,
): void {
  const { ready = true, getId, resolveMissing, onMissing } = options ?? {};
  const [searchParams, setSearchParams] = useSearchParams();

  // Effect events, not refs: both are read only from the effect below and must see the
  // latest closure without becoming a dependency of it.
  const openItem = useEffectEvent((item: T) => onOpen(item));
  const readId = useEffectEvent((item: T) =>
    getId ? getId(item) : (item as { id: string }).id,
  );
  const fetchMissing = useEffectEvent((id: string) =>
    resolveMissing ? resolveMissing(id) : null,
  );
  const reportMissing = useEffectEvent((id: string) => onMissing?.(id));
  const hasResolver = resolveMissing !== undefined;
  // Tracks the id we've already opened for. Guards against firing `onOpen` more
  // than once for the same id before the param-clear commits — notably React
  // StrictMode's double-invoked mount effect, which re-runs before the URL updates.
  const handledIdRef = useRef<string | null>(null);

  useEffect(() => {
    const editId = searchParams.get("edit");
    // Param gone: reset so the same id can be deep-linked again later.
    if (!editId) {
      handledIdRef.current = null;
      return;
    }
    if (!ready || handledIdRef.current === editId) return;
    // An empty list is either still filling (wait) or scoped away from the id — the resolver
    // exists for the second case, so an empty list must reach it.
    if (!items?.length && !hasResolver) return;

    const clearParam = () =>
      setSearchParams(
        (prev) => {
          prev.delete("edit");
          return prev;
        },
        { replace: true },
      );

    const match = items?.find((item) => readId(item) === editId);
    if (match) {
      handledIdRef.current = editId;
      openItem(match);
      clearParam();
      return;
    }

    // No resolver: keep the old behaviour of waiting, in case the list is still filling.
    if (!hasResolver) return;

    // Deliberately not cancelled on cleanup. StrictMode double-invokes this effect on mount, and
    // a cleanup that abandoned the fetch would leave the id marked as handled with nothing shown
    // — the second pass then returns here and the link dies silently. The ref above is already
    // the guard against doing the work twice, so the fetch is simply allowed to finish.
    handledIdRef.current = editId;
    void Promise.resolve(fetchMissing(editId))
      .then((fetched) => {
        if (fetched) openItem(fetched);
        else reportMissing(editId);
      })
      .catch(() => reportMissing(editId))
      .finally(clearParam);
  }, [searchParams, items, ready, setSearchParams, hasResolver]);
}
