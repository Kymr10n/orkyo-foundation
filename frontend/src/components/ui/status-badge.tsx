import { Badge } from "@foundation/src/components/ui/badge";

type BadgeVariant = "default" | "secondary" | "destructive" | "warning" | "success" | "outline";

/**
 * Single source of truth mapping a domain status string to a Badge variant so
 * the same status looks the same everywhere. Covers the union of the tenant
 * (active/suspended/deleting), user (active/disabled), and membership
 * (active/disabled/pending) vocabularies.
 *
 * Terminal/destructive states (deleting, disabled) map to `destructive` so they
 * read as clearly different from healthy or merely paused states. Unknown
 * statuses fall back to `secondary`.
 */
export function statusToVariant(status: string): BadgeVariant {
  switch (status.toLowerCase()) {
    case "active":
      return "success";
    case "pending":
      return "warning";
    case "suspended":
      return "warning";
    case "deleting":
    case "disabled":
      return "destructive";
    case "inactive":
      return "secondary";
    default:
      return "secondary";
  }
}

interface StatusBadgeProps {
  status: string;
  /** Visible text; defaults to the status string. */
  label?: string;
  className?: string;
}

export function StatusBadge({ status, label, className }: StatusBadgeProps) {
  return (
    <Badge variant={statusToVariant(status)} className={className}>
      {label ?? status}
    </Badge>
  );
}
