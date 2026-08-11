import { useQuotas } from "@foundation/src/hooks/useQuotas";
import { StorageUsageMonitor } from "./StorageUsageMonitor";
import { Card, CardContent, CardHeader, CardTitle } from "@foundation/src/components/ui/card";
import { ErrorAlert } from "@foundation/src/components/ui/ErrorAlert";
import { Skeleton } from "@foundation/src/components/ui/skeleton";
import { StatusBadge } from "@foundation/src/components/ui/status-badge";
import { SettingsPageHeader } from "./SettingsPageHeader";
import type { NumericQuota, Entitlement } from "@foundation/src/lib/api/quotas-api";
import { QUOTA_LABELS, ENTITLEMENT_LABELS, quotaSeverity } from "@foundation/src/lib/quotas/quota-display";

function formatCount(value: number): string {
  return value.toLocaleString();
}

function NumericQuotaRow({ quota }: { quota: NumericQuota }) {
  if (quota.unit === "bytes") {
    return <StorageUsageMonitor quota={quota} />;
  }

  const { unlimited, used, limit } = quota;
  const severity = quotaSeverity(quota);

  return (
    <div className="flex items-center justify-between py-1">
      <span className="text-sm text-muted-foreground">{QUOTA_LABELS[quota.key] ?? quota.key}</span>
      <span className="text-sm font-medium tabular-nums">
        <span className={severity === "exceeded" ? "text-destructive" : severity === "warning" ? "text-amber-600" : undefined}>
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
      {entitlement.enabled ? (
        <StatusBadge status="active" label="Enabled" />
      ) : (
        <StatusBadge status="inactive" label="Not available" />
      )}
    </div>
  );
}

function UsageHeader() {
  return (
    <SettingsPageHeader
      title="Usage & Limits"
      description="What this workspace is using, and the limits its plan allows. Read-only — limits change with the plan."
    />
  );
}

/**
 * Read-only "Usage & Limits" tab: what this workspace is consuming, against whatever
 * the server says its plan allows. Quotas and entitlements both come from the server,
 * so this renders what it is given rather than branching per edition.
 */
export function UsageLimitsSettings() {
  const { data, isLoading, isError } = useQuotas();

  if (isLoading) {
    return (
      <div className="space-y-6">
        <UsageHeader />
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-32 w-full" />
      </div>
    );
  }

  if (isError || !data) {
    return (
      <div className="space-y-6">
        <UsageHeader />
        <ErrorAlert message="Unable to load usage data. Try refreshing the page." />
      </div>
    );
  }

  const storageQuota = data.quotas.find((q) => q.key === "storage_bytes");
  const countQuotas = data.quotas.filter((q) => q.unit === "count");

  return (
    <div className="space-y-6">
      <UsageHeader />

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
