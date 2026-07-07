import { Badge } from "@foundation/src/components/ui/badge";
import { cn, getStatusColor, formatStatusLabel } from "@foundation/src/lib/utils/utils";

/**
 * Canonical request-status pill: the {@link getStatusColor} tint plus the
 * humanised {@link formatStatusLabel}. Replaces the copy-pasted
 * `<Badge className={getStatusColor(status)}>{formatStatusLabel(status)}</Badge>`
 * trio used across the request lists, panels and utilization surfaces.
 */
export function RequestStatusBadge({
  status,
  className,
}: {
  status: string;
  className?: string;
}) {
  return (
    <Badge className={cn(getStatusColor(status), className)}>
      {formatStatusLabel(status)}
    </Badge>
  );
}
