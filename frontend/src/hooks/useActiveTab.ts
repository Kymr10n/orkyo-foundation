import { useLocation } from 'react-router';

/**
 * Returns the active tab segment for a nested-route tabbed page.
 * Reads `/<page>/<tab>/...` and returns `<tab>`, or `defaultValue` if no segment exists.
 *
 * `segment` exists for pages whose tabs sit one level deeper because the page itself is
 * parameterised — `/resources/<typeKey>/<tab>` puts the tab at index 3, not 2.
 */
export function useActiveTab(defaultValue: string, segment = 2): string {
  const { pathname } = useLocation();
  return pathname.split('/')[segment] || defaultValue;
}
