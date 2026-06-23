import { ErrorAlert } from "@foundation/src/components/ui/ErrorAlert";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { KpiCard } from "@foundation/src/components/insights/KpiCard";
import { InsightsFilters, resolveRange, type RangePreset } from "@foundation/src/components/insights/InsightsFilters";
import {
  ConflictTrendChart,
  RequestStatusTrendChart,
  UtilizationTrendChart,
} from "@foundation/src/components/insights/InsightsTrendCharts";
import {
  useInsightsConflicts,
  useInsightsOverview,
  useInsightsRequests,
  useInsightsUtilization,
} from "@foundation/src/hooks/useInsights";
import type { InsightsBucket } from "@foundation/src/lib/api/insights-api";
import { useAppStore } from "@foundation/src/store/app-store";
import { format, parseISO } from "date-fns";
import { useMemo, useState } from "react";

// Overbooked utilization can exceed 100%; clamp the headline display to 100% (overbooking is
// surfaced via the conflict KPIs/charts instead). API value stays truthful.
const pct = (v: number | null) => (v == null ? "—" : `${Math.min(v, 100)}%`);

export function InsightsTab() {
  const selectedSiteId = useAppStore((s) => s.selectedSiteId);
  const [range, setRange] = useState<RangePreset>("window");
  const [bucket, setBucket] = useState<InsightsBucket>("month");

  // Anchor the window when the preset (or site) changes — not on every render, so query keys
  // stay stable and the dashboard doesn't refetch in a loop.
  const { from, to } = useMemo(() => resolveRange(range), [range]);

  const overview = useInsightsOverview(selectedSiteId, from, to);
  const spaceUtil = useInsightsUtilization("space", selectedSiteId, from, to, bucket);
  const peopleUtil = useInsightsUtilization("person", selectedSiteId, from, to, bucket);
  const conflicts = useInsightsConflicts(selectedSiteId, from, to, bucket);
  const requests = useInsightsRequests(selectedSiteId, from, to, bucket);

  const o = overview.data;

  return (
    <div className="h-full space-y-4 overflow-auto p-1">
      <InsightsFilters range={range} onRangeChange={setRange} bucket={bucket} onBucketChange={setBucket} />

      {/* KPI cards */}
      {overview.isLoading ? (
        <LoadingSpinner fullScreen={false} message="Loading insights…" />
      ) : overview.error ? (
        <ErrorAlert message="Could not load insights for this period." />
      ) : o ? (
        <div className="grid grid-cols-2 gap-3 md:grid-cols-3 lg:grid-cols-6">
          <KpiCard label="Total requests" value={String(o.requests.total)} />
          <KpiCard label="Scheduled" value={String(o.requests.scheduled)} />
          <KpiCard label="Unscheduled" value={String(o.requests.unscheduled)} />
          <KpiCard label="Conflicts" value={String(o.conflicts.total)} hint={`${o.conflicts.overbooking} overbooking`} />
          <KpiCard label="Space utilization" value={pct(o.utilization.spacesPercent)} />
          <KpiCard label="People utilization" value={pct(o.utilization.peoplePercent)} />
        </div>
      ) : null}

      {/* Trend charts */}
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        <UtilizationTrendChart
          title="Space utilization trend"
          data={spaceUtil.data}
          bucket={bucket}
          isLoading={spaceUtil.isLoading}
          error={spaceUtil.error}
        />
        <UtilizationTrendChart
          title="People utilization trend"
          data={peopleUtil.data}
          bucket={bucket}
          isLoading={peopleUtil.isLoading}
          error={peopleUtil.error}
        />
        <ConflictTrendChart
          data={conflicts.data}
          bucket={bucket}
          isLoading={conflicts.isLoading}
          error={conflicts.error}
        />
        <RequestStatusTrendChart
          data={requests.data}
          bucket={bucket}
          isLoading={requests.isLoading}
          error={requests.error}
        />
      </div>

      {/* Admin transparency: where the numbers came from. */}
      {o && (
        <div className="pt-1 text-right text-xs text-muted-foreground">
          Calculated at {format(parseISO(o.metadata.calculatedAt), "d MMM yyyy HH:mm")} · Source: {o.metadata.sourceMode}
        </div>
      )}
    </div>
  );
}
