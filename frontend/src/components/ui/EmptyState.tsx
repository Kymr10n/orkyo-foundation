import type { ReactNode } from "react";
import { cn } from "@foundation/src/lib/utils";

interface EmptyStateProps {
  message: ReactNode;
  /** Optional icon rendered above the message. */
  icon?: ReactNode;
  /** Optional action (e.g. a button) rendered below the message. */
  action?: ReactNode;
  className?: string;
}

/**
 * Canonical centered "nothing here" affordance: muted text in a padded,
 * centered block. Matches the empty branch used by OrkyoDataTable.
 */
export function EmptyState({ message, icon, action, className }: EmptyStateProps) {
  return (
    <div className={cn("text-center py-8 text-muted-foreground", className)}>
      {icon && <div className="mb-3 flex justify-center">{icon}</div>}
      {message}
      {action && <div className="mt-4 flex justify-center">{action}</div>}
    </div>
  );
}
