import { Card, CardContent, CardHeader, CardTitle } from "@foundation/src/components/ui/card";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { ErrorAlert } from "@foundation/src/components/ui/ErrorAlert";
import type {
  InsightsBottlenecks,
  InsightsBucket,
  InsightsConflicts,
  InsightsRequests,
  InsightsUtilization,
} from "@foundation/src/lib/api/insights-api";
import { format, parseISO } from "date-fns";
import { DATE_FORMATS } from "@foundation/src/lib/formatters";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

// Fixed palette so charts render with stable colours regardless of theme-variable availability.
const COLORS = {
  utilization: "#2563eb",
  overbooking: "#ef4444",
  criteriaMismatch: "#f59e0b",
  resourceUnavailable: "#8b5cf6",
  scheduleOutsideAvailability: "#0ea5e9",
  missingResource: "#14b8a6",
  sequenceViolation: "#ec4899",
  overbooked: "#ef4444",
  new: "#2563eb",
  inProgress: "#0ea5e9",
  done: "#10b981",
  deferred: "#64748b",
  cancelled: "#ef4444",
};

// Utilization can exceed 100% when resources are overbooked; the dashboard clamps the display to
// 100% (the overbooking signal is surfaced by the conflict charts/counts instead).
const UTILIZATION_MAX = 100;
const clampUtilization = (v: number | null) => (v == null ? null : Math.min(v, UTILIZATION_MAX));

function bucketLabel(iso: string, bucket: InsightsBucket): string {
  const d = parseISO(iso);
  switch (bucket) {
    case "week": return format(d, DATE_FORMATS.DATE_HEADER);
    case "month": return format(d, DATE_FORMATS.MONTH_YEAR);
    case "quarter": return format(d, DATE_FORMATS.QUARTER_YEAR);
    case "year": return format(d, DATE_FORMATS.YEAR);
  }
}

/**
 * Phone-tuned chart geometry. On phone (<md) recharts crowds badly: axis ticks
 * collide and the plot loses width to the Y-axis gutter. Branch on the device
 * class (the sanctioned rendering-config use of useBreakpoint) to tighten
 * margins, shrink ticks, thin the axes, keep only the first/last X tick, and
 * shrink the legend that sits below the plot.
 */
function useChartResponsive() {
  const { isPhone } = useBreakpoint();
  return {
    isPhone,
    margin: isPhone
      ? { top: 8, right: 8, bottom: 0, left: -18 }
      : { top: 8, right: 16, bottom: 0, left: -8 },
    axisFontSize: isPhone ? 10 : 12,
    xAxisInterval: isPhone ? ("preserveStartEnd" as const) : undefined,
    xAxisMinTickGap: isPhone ? 24 : 5,
    yAxisWidth: isPhone ? 32 : undefined,
    legendStyle: isPhone ? { fontSize: 11 } : undefined,
  };
}

interface ChartCardProps {
  /**
   * Height of the plot area. Trends read fine at a fixed height; a ranking has to grow with its
   * rows, or the axis silently drops labels to make them fit and names go missing.
   */
  heightClass?: string;
  title: string;
  isLoading: boolean;
  error: unknown;
  isEmpty: boolean;
  emptyMessage: string;
  /** Rendered opposite the title — a filter that narrows this chart. */
  action?: React.ReactNode;
  children: React.ReactElement;
}

/** Shared chart frame: title + the loading→error→empty→content state ladder. */
function ChartCard({ title, isLoading, error, isEmpty, emptyMessage, action, children, heightClass = "h-64" }: ChartCardProps) {
  return (
    <Card>
      <CardHeader className="pb-2 md:pb-2">
        <div className="flex items-center justify-between gap-2">
          <CardTitle className="text-sm">{title}</CardTitle>
          {action}
        </div>
      </CardHeader>
      <CardContent className={heightClass}>
        {isLoading ? (
          <LoadingSpinner fullScreen={false} message="Loading…" />
        ) : error ? (
          <ErrorAlert message="Could not load this chart." />
        ) : isEmpty ? (
          <div className="flex h-full items-center justify-center text-center text-sm text-muted-foreground">
            {emptyMessage}
          </div>
        ) : (
          <ResponsiveContainer width="100%" height="100%">
            {children}
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  );
}

// ── Bottlenecks (most overloaded resources) ──────────────────────────────────

/**
 * The resources booked past their capacity, worst first.
 *
 * A horizontal bar chart because the ranking is the message: names sit on the axis where they
 * are readable at any length, and the eye reads the order down the column without a legend.
 *
 * Hours, not minutes — a planner talks in hours, and "1440 minutes overbooked" is arithmetic the
 * reader should not have to do.
 */
export function BottleneckChart({
  data, isLoading, error, title = "Most overloaded resources", action,
}: {
  data?: InsightsBottlenecks;
  isLoading: boolean;
  error?: unknown;
  /** Names the class, or the single resource type the chart is narrowed to. */
  title?: string;
  /** The resource-type filter for this chart. */
  action?: React.ReactNode;
}) {
  const r = useChartResponsive();
  const items = data?.items ?? [];

  // Keyed by id, not name: two resources can share a display name, and recharts merges
  // categories that compare equal — which silently collapses two bars into one.
  const rows = items.map((item) => ({
    id: item.resourceId,
    name: item.name,
    hours: Math.round((item.overbookedMinutes / 60) * 10) / 10,
    peak: item.peakUtilizationPercent == null ? null : Math.round(item.peakUtilizationPercent),
    type: item.resourceTypeDisplayName,
  }));
  const nameById = new Map(rows.map((row) => [row.id, row.name]));

  return (
    <ChartCard
      title={title}
      action={action}
      isLoading={isLoading}
      error={error}
      // ~34px a row plus axis: enough that every name has somewhere to sit.
      heightClass={rows.length > 6 ? "h-96" : "h-64"}
      isEmpty={rows.length === 0}
      // An empty list is the healthy answer here, so it says so rather than reading as a gap
      // in the data.
      emptyMessage="No resource was booked beyond its capacity in this period."
    >
      <BarChart data={rows} layout="vertical" margin={{ top: 8, right: 16, bottom: 16, left: 8 }}>
        <CartesianGrid strokeDasharray="3 3" horizontal={false} />
        <XAxis
          type="number"
          tick={{ fontSize: r.axisFontSize }}
          label={r.isPhone ? undefined : { value: "Hours overbooked", position: "insideBottom", offset: -4, fontSize: 11 }}
        />
        <YAxis
          type="category"
          dataKey="id"
          tickFormatter={(id: string) => nameById.get(id) ?? ""}
          width={r.isPhone ? 80 : 140}
          tick={{ fontSize: r.axisFontSize }}
          // Every bar is a resource somebody has to act on; letting recharts thin the labels
          // leaves bars with no name against them.
          interval={0}
        />
        <Tooltip
          formatter={(value, _name, entry) => {
            const row = entry?.payload as { peak?: number | null } | undefined;
            // No capacity published means no percentage to quote, so the peak clause drops out
            // rather than claiming 0%.
            const peak = row?.peak == null ? "" : ` (peak ${row.peak}%)`;
            return [`${value} h over capacity${peak}`, "Overbooked"];
          }}
          labelFormatter={(_label, payload) => {
            // Read the name off the row rather than the label: recharts types the label as
            // ReactNode, and stringifying that is how "[object Object]" reaches a tooltip.
            const row = payload?.[0]?.payload as { name?: string; type?: string } | undefined;
            return row?.type ? `${row.name ?? ""} — ${row.type}` : (row?.name ?? "");
          }}
        />
        <Bar dataKey="hours" fill={COLORS.overbooked} radius={[0, 4, 4, 0]} />
      </BarChart>
    </ChartCard>
  );
}

// ── Utilization trend (one resource type) ────────────────────────────────────

export function UtilizationTrendChart({
  title, data, bucket, isLoading, error,
}: {
  title: string;
  data: InsightsUtilization | undefined;
  bucket: InsightsBucket;
  isLoading: boolean;
  error: unknown;
}) {
  const r = useChartResponsive();
  const series = data?.series ?? [];
  const isEmpty = series.length === 0
    || series.every((p) => p.utilizationPercent == null && p.totalCapacityMinutes === 0);
  const chartData = series.map((p) => ({
    label: bucketLabel(p.bucketStart, bucket),
    utilization: clampUtilization(p.utilizationPercent),
  }));

  return (
    <ChartCard
      title={title}
      isLoading={isLoading}
      error={error}
      isEmpty={isEmpty}
      emptyMessage="No capacity configured for this period."
    >
      <LineChart data={chartData} margin={r.margin}>
        <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
        <XAxis dataKey="label" fontSize={r.axisFontSize} interval={r.xAxisInterval} minTickGap={r.xAxisMinTickGap} />
        <YAxis fontSize={r.axisFontSize} width={r.yAxisWidth} unit="%" domain={[0, UTILIZATION_MAX]} />
        <Tooltip formatter={(v) => (v == null ? "—" : `${v}%`)} />
        <Line
          type="monotone"
          dataKey="utilization"
          name="Utilization"
          stroke={COLORS.utilization}
          strokeWidth={2}
          dot={false}
          connectNulls={false}
        />
      </LineChart>
    </ChartCard>
  );
}

// ── Conflict trend (stacked by type) ─────────────────────────────────────────

export function ConflictTrendChart({
  data, bucket, isLoading, error,
}: {
  data: InsightsConflicts | undefined;
  bucket: InsightsBucket;
  isLoading: boolean;
  error: unknown;
}) {
  const r = useChartResponsive();
  const series = data?.series ?? [];
  const isEmpty = series.length === 0 || series.every((p) => p.total === 0);
  const chartData = series.map((p) => ({
    label: bucketLabel(p.bucketStart, bucket),
    Overbooking: p.overbooking,
    "Criteria mismatch": p.criteriaMismatch,
    "Resource unavailable": p.resourceUnavailable,
    "Outside availability": p.scheduleOutsideAvailability,
    // Every counted kind gets a bar: the stack is read as a breakdown of `total`, so an
    // omitted kind silently shortens the column.
    "Missing resource": p.missingResource,
    "Sequence violation": p.sequenceViolation,
  }));

  return (
    <ChartCard
      title="Conflict trend"
      isLoading={isLoading}
      error={error}
      isEmpty={isEmpty}
      emptyMessage="No conflicts in this period."
    >
      <BarChart data={chartData} margin={r.margin}>
        <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
        <XAxis dataKey="label" fontSize={r.axisFontSize} interval={r.xAxisInterval} minTickGap={r.xAxisMinTickGap} />
        <YAxis fontSize={r.axisFontSize} width={r.yAxisWidth} allowDecimals={false} />
        <Tooltip />
        <Legend position="bottom" wrapperStyle={r.legendStyle} />
        <Bar dataKey="Overbooking" stackId="c" fill={COLORS.overbooking} />
        <Bar dataKey="Criteria mismatch" stackId="c" fill={COLORS.criteriaMismatch} />
        <Bar dataKey="Resource unavailable" stackId="c" fill={COLORS.resourceUnavailable} />
        <Bar dataKey="Outside availability" stackId="c" fill={COLORS.scheduleOutsideAvailability} />
        <Bar dataKey="Missing resource" stackId="c" fill={COLORS.missingResource} />
        <Bar dataKey="Sequence violation" stackId="c" fill={COLORS.sequenceViolation} />
      </BarChart>
    </ChartCard>
  );
}

// ── Request status trend (stacked) ───────────────────────────────────────────

export function RequestStatusTrendChart({
  data, bucket, isLoading, error,
}: {
  data: InsightsRequests | undefined;
  bucket: InsightsBucket;
  isLoading: boolean;
  error: unknown;
}) {
  const r = useChartResponsive();
  const series = data?.series ?? [];
  const isEmpty = series.length === 0 || series.every((p) => p.total === 0);
  // Bucketed by scheduled date and stacked by real domain status. Backlog (no scheduled date) isn't
  // on a timeline, so it's not in this chart — it's the overview "Unscheduled" KPI.
  const chartData = series.map((p) => ({
    label: bucketLabel(p.bucketStart, bucket),
    New: p.new,
    "In progress": p.inProgress,
    Done: p.done,
    Deferred: p.deferred,
    Canceled: p.cancelled,
  }));

  return (
    <ChartCard
      title="Request status trend"
      isLoading={isLoading}
      error={error}
      isEmpty={isEmpty}
      emptyMessage="No scheduled requests in this period."
    >
      <BarChart data={chartData} margin={r.margin}>
        <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
        <XAxis dataKey="label" fontSize={r.axisFontSize} interval={r.xAxisInterval} minTickGap={r.xAxisMinTickGap} />
        <YAxis fontSize={r.axisFontSize} width={r.yAxisWidth} allowDecimals={false} />
        <Tooltip />
        <Legend position="bottom" wrapperStyle={r.legendStyle} />
        <Bar dataKey="New" stackId="r" fill={COLORS.new} />
        <Bar dataKey="In progress" stackId="r" fill={COLORS.inProgress} />
        <Bar dataKey="Done" stackId="r" fill={COLORS.done} />
        <Bar dataKey="Deferred" stackId="r" fill={COLORS.deferred} />
        <Bar dataKey="Canceled" stackId="r" fill={COLORS.cancelled} />
      </BarChart>
    </ChartCard>
  );
}
