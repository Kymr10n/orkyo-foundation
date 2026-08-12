import { useEffect, useEffectEvent, useRef } from "react";
import { useSearchParams } from "react-router";

interface UseEditQueryParamOptions<T> {
  /** Skip while the list is still loading; defaults to true (ready). */
  ready?: boolean;
  /** How to read an item's id; defaults to `item.id`. */
  getId?: (item: T) => string;
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
  const { ready = true, getId } = options ?? {};
  const [searchParams, setSearchParams] = useSearchParams();

  // Effect events, not refs: both are read only from the effect below and must see the
  // latest closure without becoming a dependency of it.
  const openItem = useEffectEvent((item: T) => onOpen(item));
  const readId = useEffectEvent((item: T) =>
    getId ? getId(item) : (item as { id: string }).id,
  );
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
    if (!ready || !items?.length || handledIdRef.current === editId) return;

    const match = items.find((item) => readId(item) === editId);
    if (!match) return;

    handledIdRef.current = editId;
    openItem(match);
    setSearchParams(
      (prev) => {
        prev.delete("edit");
        return prev;
      },
      { replace: true },
    );
  }, [searchParams, items, ready, setSearchParams]);
}
