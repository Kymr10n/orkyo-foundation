import * as React from "react"

import { cn } from "@foundation/src/lib/utils"

/**
 * Horizontal or vertical rule.
 *
 * Replaces @radix-ui/react-separator, which for this component was a styled `<div>`
 * plus the ARIA rules reproduced below: a decorative separator is hidden from the
 * accessibility tree with `role="none"`, and a meaningful one is exposed as
 * `role="separator"` — carrying `aria-orientation` only when vertical, because
 * horizontal is the ARIA default.
 */
const Separator = React.forwardRef<
  HTMLDivElement,
  React.ComponentPropsWithoutRef<"div"> & {
    orientation?: "horizontal" | "vertical"
    decorative?: boolean
  }
>(
  (
    { className, orientation = "horizontal", decorative = true, ...props },
    ref
  ) => (
    <div
      ref={ref}
      data-orientation={orientation}
      {...(decorative
        ? { role: "none" as const }
        : {
            role: "separator" as const,
            "aria-orientation": orientation === "vertical" ? orientation : undefined,
          })}
      className={cn(
        "shrink-0 bg-border",
        orientation === "horizontal" ? "h-[1px] w-full" : "h-full w-[1px]",
        className
      )}
      {...props}
    />
  )
)
Separator.displayName = "Separator"

export { Separator }
