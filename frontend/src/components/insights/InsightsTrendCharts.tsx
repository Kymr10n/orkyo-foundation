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
  scheduled: "#2563eb",
  unscheduled: "#94a3b8",
  cancelled: "#ef4444",
};

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
    utilization: p.utilizationPercent,
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
        <YAxis fontSize={12} unit="%" />
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
  // Scheduled + Unscheduled + Cancelled partition the total (Completed is a status view of Scheduled,
  // so it is omitted from the stack to avoid double-counting).
  const chartData = series.map((p) => ({
    label: bucketLabel(p.bucketStart, bucket),
    Scheduled: p.scheduled,
    Unscheduled: p.unscheduled,
    Cancelled: p.cancelled,
  }));

  return (
    <ChartCard
      title="Request status trend"
      isLoading={isLoading}
      error={error}
      isEmpty={isEmpty}
      emptyMessage="No requests in this period."
    >
      <BarChart data={chartData} margin={{ top: 8, right: 16, bottom: 0, left: -8 }}>
        <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
        <XAxis dataKey="label" fontSize={12} />
        <YAxis fontSize={12} allowDecimals={false} />
        <Tooltip />
        <Legend />
        <Bar dataKey="Scheduled" stackId="r" fill={COLORS.scheduled} />
        <Bar dataKey="Unscheduled" stackId="r" fill={COLORS.unscheduled} />
        <Bar dataKey="Cancelled" stackId="r" fill={COLORS.cancelled} />
      </BarChart>
    </ChartCard>
  );
}
