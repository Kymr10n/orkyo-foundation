import { cn } from "@foundation/src/lib/utils";
import type { NumericQuota } from "@foundation/src/lib/api/quotas-api";
import { formatBytes, quotaSeverity } from "@foundation/src/lib/quotas/quota-display";

interface StorageUsageMonitorProps {
  quota: NumericQuota;
  className?: string;
}

/**
 * Storage usage bar with threshold colouring.
 * Reused by both SaaS (quota-enforced) and Community (display-only, unlimited).
 */
export function StorageUsageMonitor({ quota, className }: StorageUsageMonitorProps) {
  const { used, limit, unlimited, percentUsed } = quota;

  const pct = Math.min(percentUsed, 100);
  const severity = quotaSeverity(quota);
  // Full but not over (used === limit): valid, at-capacity state — amber, not red.
  const atCapacity = severity === "warning" && used >= limit;

  return (
    <div className={cn("space-y-1", className)}>
      <div className="flex justify-between text-sm">
        <span className="text-muted-foreground">Storage used</span>
        <span className={cn(
          "font-medium tabular-nums",
          severity === "exceeded" && "text-destructive",
          severity === "warning" && "text-amber-600",
        )}>
          {formatBytes(used)}
          {!unlimited && <span className="text-muted-foreground font-normal"> / {formatBytes(limit)}</span>}
          {unlimited && <span className="text-muted-foreground font-normal"> (no limit)</span>}
        </span>
      </div>
      <div className="h-2 rounded-full bg-muted overflow-hidden">
        {unlimited ? (
          <div className="h-full bg-primary/40 rounded-full" style={{ width: "100%" }} />
        ) : (
          <div
            className={cn(
              "h-full rounded-full transition-all",
              severity === "exceeded" ? "bg-destructive" : severity === "warning" ? "bg-amber-500" : "bg-primary",
            )}
            style={{ width: `${pct}%` }}
          />
        )}
      </div>
      {!unlimited && (
        <p className={cn(
          "text-xs",
          severity === "exceeded" ? "text-destructive" : severity === "warning" ? "text-amber-600" : "text-muted-foreground",
        )}>
          {severity === "exceeded"
            ? "Storage over limit — uploads are blocked until usage is reduced"
            : atCapacity
              ? `At capacity — ${formatBytes(limit)} used`
              : severity === "warning"
                ? `${pct.toFixed(0)}% used — approaching limit`
                : `${pct.toFixed(0)}% of ${formatBytes(limit)} used`}
        </p>
      )}
    </div>
  );
}
