import { useQuotas } from "@foundation/src/hooks/useQuotas";
import { StorageUsageMonitor } from "./StorageUsageMonitor";
import { Card, CardContent, CardHeader, CardTitle } from "@foundation/src/components/ui/card";
import { Badge } from "@foundation/src/components/ui/badge";
import type { NumericQuota, Entitlement } from "@foundation/src/lib/api/quotas-api";
import { QUOTA_LABELS, ENTITLEMENT_LABELS } from "@foundation/src/lib/quotas/quota-display";

function formatCount(value: number): string {
  return value.toLocaleString();
}

function NumericQuotaRow({ quota }: { quota: NumericQuota }) {
  if (quota.unit === "bytes") {
    return <StorageUsageMonitor quota={quota} />;
  }

  const { unlimited, used, limit, percentUsed } = quota;
  // A quota is only *violated* when usage exceeds the limit; being exactly at the
  // limit (e.g. 1/1) is valid, full usage — not a violation.
  const isExceeded = !unlimited && used > limit;
  const isWarning = !unlimited && !isExceeded && percentUsed >= 80;

  return (
    <div className="flex items-center justify-between py-1">
      <span className="text-sm text-muted-foreground">{QUOTA_LABELS[quota.key] ?? quota.key}</span>
      <span className="text-sm font-medium tabular-nums">
        <span className={isExceeded ? "text-destructive" : isWarning ? "text-amber-600" : undefined}>
          {formatCount(used)}
        </span>
        {!unlimited && (
          <span className="text-muted-foreground font-normal"> / {formatCount(limit)}</span>
        )}
        {unlimited && (
          <span className="text-muted-foreground font-normal"> (no limit)</span>
        )}
      </span>
    </div>
  );
}

function EntitlementRow({ entitlement }: { entitlement: Entitlement }) {
  return (
    <div className="flex items-center justify-between py-1">
      <span className="text-sm text-muted-foreground">
        {ENTITLEMENT_LABELS[entitlement.key] ?? entitlement.key}
      </span>
      <Badge variant={entitlement.enabled ? "default" : "secondary"}>
        {entitlement.enabled ? "Enabled" : "Not available"}
      </Badge>
    </div>
  );
}

/**
 * Read-only "Usage & Limits" settings tab.
 * SaaS: shows all quotas + entitlements with live usage.
 * Community: shows only storage (no limit enforced).
 */
export function UsageLimitsSettings() {
  const { data, isLoading, isError } = useQuotas();

  if (isLoading) {
    return (
      <div className="space-y-4 p-4 max-w-2xl">
        <div className="h-32 w-full rounded-md bg-muted animate-pulse" />
        <div className="h-32 w-full rounded-md bg-muted animate-pulse" />
      </div>
    );
  }

  if (isError || !data) {
    return (
      <div className="p-4 text-sm text-muted-foreground">
        Unable to load usage data. Try refreshing the page.
      </div>
    );
  }

  const storageQuota = data.quotas.find((q) => q.key === "storage_bytes");
  const countQuotas = data.quotas.filter((q) => q.unit === "count");

  return (
    <div className="space-y-6 p-4 max-w-2xl">
      {storageQuota && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Storage</CardTitle>
          </CardHeader>
          <CardContent>
            <StorageUsageMonitor quota={storageQuota} />
          </CardContent>
        </Card>
      )}

      {countQuotas.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Usage limits</CardTitle>
          </CardHeader>
          <CardContent className="divide-y">
            {countQuotas.map((q) => (
              <NumericQuotaRow key={q.key} quota={q} />
            ))}
          </CardContent>
        </Card>
      )}

      {data.entitlements.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Features</CardTitle>
          </CardHeader>
          <CardContent className="divide-y">
            {data.entitlements.map((e) => (
              <EntitlementRow key={e.key} entitlement={e} />
            ))}
          </CardContent>
        </Card>
      )}
    </div>
  );
}
