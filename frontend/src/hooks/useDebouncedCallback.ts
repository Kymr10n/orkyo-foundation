import { useCallback, useEffect, useMemo, useRef } from "react";

export interface DebouncedCallback<A extends unknown[]> {
  (...args: A): void;
  /** Cancel any pending invocation. */
  cancel: () => void;
}

/**
 * Returns a stable debounced version of `callback` that fires at most once per
 * `delay` ms of quiet. The returned function keeps a stable identity (safe to
 * pass as a prop / effect dependency) and always calls the latest `callback`.
 * Any pending timer is cleared on unmount, and can be cleared early via
 * `.cancel()`.
 */
export function useDebouncedCallback<A extends unknown[]>(
  callback: (...args: A) => void,
  delay: number,
): DebouncedCallback<A> {
  const callbackRef = useRef(callback);
  // Synced in an effect, not during render. The debounced call fires a whole `delay` later,
  // by which time the effect has long flushed, so it still sees the latest callback.
  useEffect(() => {
    callbackRef.current = callback;
  });
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const cancel = useCallback(() => {
    if (timeoutRef.current) {
      clearTimeout(timeoutRef.current);
      timeoutRef.current = undefined;
    }
  }, []);

  useEffect(() => cancel, [cancel]);

  // Built inside the memo so `.cancel` is attached while the function is still a local
  // value — attaching it afterwards would modify a value already returned by a hook.
  return useMemo(() => {
    const debounced = ((...args: A) => {
      if (timeoutRef.current) clearTimeout(timeoutRef.current);
      timeoutRef.current = setTimeout(() => {
        timeoutRef.current = undefined;
        callbackRef.current(...args);
      }, delay);
    }) as DebouncedCallback<A>;
    debounced.cancel = cancel;
    return debounced;
  }, [delay, cancel]);
}
