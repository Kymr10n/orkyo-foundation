import { FeatureKeys } from "@foundation/contracts/plans";
import { useFeatureEnabled } from "@foundation/src/hooks/useFeatureEnabled";

/**
 * Whether the current tenant is entitled to calendar subscriptions (.ics feeds).
 *
 * Presentation only — the server enforces `FeatureKeys.CalendarFeed` on both the
 * create endpoint (402) and the feed itself (404). This just decides whether the
 * dialog shows the feature or the upsell.
 */
export function useCalendarFeedAvailable(): boolean {
  return useFeatureEnabled(FeatureKeys.CalendarFeed);
}
