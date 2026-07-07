import type { HTMLAttributes } from "react";
import { cn } from "@foundation/src/lib/utils";

/**
 * Content-shaped loading placeholder. Use for in-region loads where the final
 * layout is known (table rows, cards) so the shell stays stable while data
 * arrives. For route/auth/full-region loads use {@link LoadingSpinner} instead.
 *
 * Honours `prefers-reduced-motion` by disabling the pulse.
 */
export function Skeleton({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn("bg-muted animate-pulse rounded-md motion-reduce:animate-none", className)}
      {...props}
    />
  );
}
