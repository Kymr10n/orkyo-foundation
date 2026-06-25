import { useOutletContext } from 'react-router-dom';
import type { InsightsBucket } from '@foundation/src/lib/api/insights-api';

/**
 * Filter state shared by every Insights tab. Provided once by InsightsPage via the router
 * <Outlet context> so the range/bucket/site filter is defined in a single place and each tab
 * reads it (and runs only its own queries) — no duplicated filter UI or state.
 */
export interface InsightsTabContext {
  from: Date;
  to: Date;
  bucket: InsightsBucket;
  siteId: string | null;
}

export const useInsightsTabContext = () => useOutletContext<InsightsTabContext>();
