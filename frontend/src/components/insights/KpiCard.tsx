import { Card, CardContent } from "@foundation/src/components/ui/card";

interface KpiCardProps {
  label: string;
  value: string;
  hint?: string;
}

/** A single executive KPI tile. Value is pre-formatted by the caller ("—" for unknown). */
export function KpiCard({ label, value, hint }: KpiCardProps) {
  return (
    <Card>
      <CardContent className="p-4">
        <div className="text-sm text-muted-foreground">{label}</div>
        <div className="mt-1 text-2xl font-semibold tabular-nums">{value}</div>
        {hint && <div className="mt-1 text-xs text-muted-foreground">{hint}</div>}
      </CardContent>
    </Card>
  );
}
