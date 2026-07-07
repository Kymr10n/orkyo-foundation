import { useCallback } from "react";
import { useSearchParams } from "react-router-dom";

/**
 * Syncs an in-page tab selection with the `?tab=` query param.
 *
 * The active tab is derived directly from the URL (the URL is the single source
 * of truth), so it survives reloads and back/forward navigation. `setTab` writes
 * the param with `{ replace: true }` so switching tabs doesn't stack history
 * entries.
 *
 * For top-level page sections that live on their own route segment, use
 * {@link useActiveTab} instead. See docs/UI-GUIDELINES.md.
 */
export function useTabParam(defaultTab: string): [string, (tab: string) => void] {
  const [searchParams, setSearchParams] = useSearchParams();
  const activeTab = searchParams.get("tab") ?? defaultTab;

  const setTab = useCallback(
    (tab: string) => {
      const next = new URLSearchParams(searchParams);
      next.set("tab", tab);
      setSearchParams(next, { replace: true });
    },
    [searchParams, setSearchParams],
  );

  return [activeTab, setTab];
}
