import * as React from "react"
import { cn } from "@foundation/src/lib/utils"

/**
 * Form label.
 *
 * Replaces @radix-ui/react-label, which contributed exactly one behaviour over a
 * native element: suppressing the text selection that a double-click on the label
 * produces. That is reproduced below. Click-to-focus and the screen-reader
 * association come from native `<label htmlFor>`, so nothing else was lost.
 */
const Label = React.forwardRef<
  HTMLLabelElement,
  React.ComponentPropsWithoutRef<"label">
>(({ className, onMouseDown, ...props }, ref) => (
  <label
    ref={ref}
    className={cn(
      "text-sm font-medium leading-none peer-disabled:cursor-not-allowed peer-disabled:opacity-70",
      className
    )}
    {...props}
    onMouseDown={(event) => {
      // A click that lands on a real control inside the label belongs to that control.
      if ((event.target as HTMLElement).closest("button, input, select, textarea")) return;
      onMouseDown?.(event);
      // The second and later clicks of a multi-click would select the label's text.
      if (!event.defaultPrevented && event.detail > 1) event.preventDefault();
    }}
  />
))
Label.displayName = "Label"

export { Label }
