import { useCallback, useEffect, useRef } from "react";

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
  callbackRef.current = callback;
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const cancel = useCallback(() => {
    if (timeoutRef.current) {
      clearTimeout(timeoutRef.current);
      timeoutRef.current = undefined;
    }
  }, []);

  useEffect(() => cancel, [cancel]);

  const debounced = useCallback(
    (...args: A) => {
      if (timeoutRef.current) clearTimeout(timeoutRef.current);
      timeoutRef.current = setTimeout(() => {
        timeoutRef.current = undefined;
        callbackRef.current(...args);
      }, delay);
    },
    [delay],
  ) as DebouncedCallback<A>;

  debounced.cancel = cancel;
  return debounced;
}
