import { useLocation } from 'react-router-dom';

/**
 * Returns the active tab segment for a nested-route tabbed page.
 * Reads `/<page>/<tab>/...` and returns `<tab>`, or `defaultValue` if no segment exists.
 */
export function useActiveTab(defaultValue: string): string {
  const { pathname } = useLocation();
  return pathname.split('/')[2] ?? defaultValue;
}
