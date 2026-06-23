import { Card, CardContent, CardHeader, CardTitle } from "@foundation/src/components/ui/card";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { ErrorAlert } from "@foundation/src/components/ui/ErrorAlert";
import type {
  InsightsBucket,
  InsightsConflicts,
  InsightsRequests,
  InsightsUtilization,
} from "@foundation/src/lib/api/insights-api";
import { format, parseISO } from "date-fns";
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
  planned: "#2563eb",
  inProgress: "#0ea5e9",
  done: "#10b981",
  cancelled: "#ef4444",
};

// Utilization can exceed 100% when resources are overbooked; the dashboard clamps the display to
// 100% (the overbooking signal is surfaced by the conflict charts/counts instead).
const UTILIZATION_MAX = 100;
const clampUtilization = (v: number | null) => (v == null ? null : Math.min(v, UTILIZATION_MAX));

function bucketLabel(iso: string, bucket: InsightsBucket): string {
  const d = parseISO(iso);
  switch (bucket) {
    case "week": return format(d, "MMM d");
    case "month": return format(d, "MMM yy");
    case "quarter": return format(d, "QQQ yyyy");
    case "year": return format(d, "yyyy");
  }
}

interface ChartCardProps {
  title: string;
  isLoading: boolean;
  error: unknown;
  isEmpty: boolean;
  emptyMessage: string;
  children: React.ReactElement;
}

/** Shared chart frame: title + the loading→error→empty→content state ladder. */
function ChartCard({ title, isLoading, error, isEmpty, emptyMessage, children }: ChartCardProps) {
  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm">{title}</CardTitle>
      </CardHeader>
      <CardContent className="h-64">
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
      <LineChart data={chartData} margin={{ top: 8, right: 16, bottom: 0, left: -8 }}>
        <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
        <XAxis dataKey="label" fontSize={12} />
        <YAxis fontSize={12} unit="%" domain={[0, UTILIZATION_MAX]} />
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
  const series = data?.series ?? [];
  const isEmpty = series.length === 0 || series.every((p) => p.total === 0);
  const chartData = series.map((p) => ({
    label: bucketLabel(p.bucketStart, bucket),
    Overbooking: p.overbooking,
    "Criteria mismatch": p.criteriaMismatch,
    "Resource unavailable": p.resourceUnavailable,
    "Outside availability": p.scheduleOutsideAvailability,
  }));

  return (
    <ChartCard
      title="Conflict trend"
      isLoading={isLoading}
      error={error}
      isEmpty={isEmpty}
      emptyMessage="No conflicts in this period."
    >
      <BarChart data={chartData} margin={{ top: 8, right: 16, bottom: 0, left: -8 }}>
        <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
        <XAxis dataKey="label" fontSize={12} />
        <YAxis fontSize={12} allowDecimals={false} />
        <Tooltip />
        <Legend />
        <Bar dataKey="Overbooking" stackId="c" fill={COLORS.overbooking} />
        <Bar dataKey="Criteria mismatch" stackId="c" fill={COLORS.criteriaMismatch} />
        <Bar dataKey="Resource unavailable" stackId="c" fill={COLORS.resourceUnavailable} />
        <Bar dataKey="Outside availability" stackId="c" fill={COLORS.scheduleOutsideAvailability} />
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
  const series = data?.series ?? [];
  const isEmpty = series.length === 0 || series.every((p) => p.total === 0);
  // Bucketed by scheduled date and stacked by real domain status. Backlog (no scheduled date) isn't
  // on a timeline, so it's not in this chart — it's the overview "Unscheduled" KPI.
  const chartData = series.map((p) => ({
    label: bucketLabel(p.bucketStart, bucket),
    Planned: p.planned,
    "In progress": p.inProgress,
    Done: p.done,
    Cancelled: p.cancelled,
  }));

  return (
    <ChartCard
      title="Request status trend"
      isLoading={isLoading}
      error={error}
      isEmpty={isEmpty}
      emptyMessage="No scheduled requests in this period."
    >
      <BarChart data={chartData} margin={{ top: 8, right: 16, bottom: 0, left: -8 }}>
        <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
        <XAxis dataKey="label" fontSize={12} />
        <YAxis fontSize={12} allowDecimals={false} />
        <Tooltip />
        <Legend />
        <Bar dataKey="Planned" stackId="r" fill={COLORS.planned} />
        <Bar dataKey="In progress" stackId="r" fill={COLORS.inProgress} />
        <Bar dataKey="Done" stackId="r" fill={COLORS.done} />
        <Bar dataKey="Cancelled" stackId="r" fill={COLORS.cancelled} />
      </BarChart>
    </ChartCard>
  );
}
