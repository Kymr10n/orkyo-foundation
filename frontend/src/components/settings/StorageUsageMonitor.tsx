import { cn } from "@foundation/src/lib/utils";
import type { NumericQuota } from "@foundation/src/lib/api/quotas-api";

function formatBytes(bytes: number): string {
  if (bytes === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return `${(bytes / Math.pow(1024, i)).toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

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
  const isWarning = !unlimited && percentUsed >= 80 && percentUsed < 100;
  const isExceeded = !unlimited && percentUsed >= 100;

  return (
    <div className={cn("space-y-1", className)}>
      <div className="flex justify-between text-sm">
        <span className="text-muted-foreground">Storage used</span>
        <span className={cn(
          "font-medium tabular-nums",
          isExceeded && "text-destructive",
          isWarning && "text-amber-600",
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
              isExceeded ? "bg-destructive" : isWarning ? "bg-amber-500" : "bg-primary",
            )}
            style={{ width: `${pct}%` }}
          />
        )}
      </div>
      {!unlimited && (
        <p className={cn(
          "text-xs",
          isExceeded ? "text-destructive" : isWarning ? "text-amber-600" : "text-muted-foreground",
        )}>
          {isExceeded
            ? "Storage limit reached — uploads are blocked until usage is reduced"
            : isWarning
              ? `${pct.toFixed(0)}% used — approaching limit`
              : `${pct.toFixed(0)}% of ${formatBytes(limit)} used`}
        </p>
      )}
    </div>
  );
}
