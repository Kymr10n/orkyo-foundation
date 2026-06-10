/**
 * Canonical display helpers for quota/entitlement keys.
 * Single source of truth shared by the tenant-facing Usage & Limits tab and the
 * site-admin quota panels — keeps labels and byte formatting consistent across screens.
 */

/** Numeric quota keys → tenant-facing label. */
export const QUOTA_LABELS: Record<string, string> = {
  active_seats: "Members",
  production_sites: "Sites",
  spaces: "Spaces",
  storage_bytes: "Storage",
};

/** Boolean entitlement keys → tenant-facing label. */
export const ENTITLEMENT_LABELS: Record<string, string> = {
  api_access_enabled: "API Access",
  audit_log_enabled: "Audit Log",
  automated_backups_enabled: "Automated Backups",
  data_export_enabled: "Data Export",
};

/** Human-readable byte size (B/KB/MB/GB/TB), one decimal above bytes. */
export function formatBytes(bytes: number): string {
  if (bytes === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return `${(bytes / Math.pow(1024, i)).toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

export type QuotaSeverity = "neutral" | "warning" | "exceeded";

/**
 * Severity of a quota's current usage, used to colour usage indicators consistently
 * across the storage bar and the count-quota rows.
 *
 * - `exceeded` (red): usage is strictly *over* the limit (`used > limit`) — a violation,
 *   e.g. 2/1 after a tier downgrade. Being exactly at the limit (1/1, 100%) is valid,
 *   full usage and is NOT exceeded.
 * - `warning` (amber): at or near capacity (80%–100%, inclusive of full).
 * - `neutral`: below 80%, or unlimited.
 */
export function quotaSeverity(
  q: { unlimited: boolean; used: number; limit: number; percentUsed: number },
): QuotaSeverity {
  if (q.unlimited) return "neutral";
  if (q.used > q.limit) return "exceeded";
  if (q.percentUsed >= 80) return "warning";
  return "neutral";
}
