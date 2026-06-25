import { format, parseISO } from 'date-fns';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import { KpiCard } from '@foundation/src/components/insights/KpiCard';
import { RequestStatusTrendChart } from '@foundation/src/components/insights/InsightsTrendCharts';
import { useInsightsOverview, useInsightsRequests } from '@foundation/src/hooks/useInsights';
import { useInsightsTabContext } from '@foundation/src/components/insights/insightsTabContext';

// Overbooked utilization can exceed 100%; clamp the headline display to 100% (overbooking is
// surfaced via the conflict KPIs/charts instead). API value stays truthful.
const pct = (v: number | null) => (v == null ? '—' : `${Math.min(v, 100)}%`);

export function OverviewTab() {
  const { from, to, bucket, siteId } = useInsightsTabContext();
  const overview = useInsightsOverview(siteId, from, to);
  const requests = useInsightsRequests(siteId, from, to, bucket);

  const o = overview.data;

  return (
    <div className="h-full space-y-4 overflow-auto p-1">
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

      <RequestStatusTrendChart
        data={requests.data}
        bucket={bucket}
        isLoading={requests.isLoading}
        error={requests.error}
      />

      {/* Admin transparency: where the numbers came from. */}
      {o && (
        <div className="pt-1 text-right text-xs text-muted-foreground">
          Calculated at {format(parseISO(o.metadata.calculatedAt), 'd MMM yyyy HH:mm')} · Source: {o.metadata.sourceMode}
        </div>
      )}
    </div>
  );
}
