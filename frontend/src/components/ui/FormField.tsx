import type { ReactNode } from "react";
import { Label } from "@foundation/src/components/ui/label";
import { cn } from "@foundation/src/lib/utils";

export interface FormFieldProps {
  /** Wires the label's `htmlFor` to the control for accessibility. */
  htmlFor?: string;
  /** Field label. */
  label: ReactNode;
  /** Appends a required marker (`*` in `text-destructive`) after the label. */
  required?: boolean;
  /** Optional help text rendered under the control (`text-xs text-muted-foreground`). */
  help?: ReactNode;
  /** The input control (Input, Select, Textarea, …). */
  children: ReactNode;
  /** Override the default vertical rhythm (`space-y-2`). */
  className?: string;
}

/**
 * The standard label + control + help cluster for form dialogs. Owns only the
 * presentation of the required asterisk and help text — no validation logic —
 * so the handful of hand-rolled `<div className="space-y-2"><Label>…</Label>…`
 * clusters stay consistent (and required fields are marked uniformly).
 */
export function FormField({
  htmlFor,
  label,
  required,
  help,
  children,
  className,
}: FormFieldProps) {
  return (
    <div className={cn("space-y-2", className)}>
      <Label htmlFor={htmlFor}>
        {label}
        {required && <span className="text-destructive"> *</span>}
      </Label>
      {children}
      {help && <p className="text-xs text-muted-foreground">{help}</p>}
    </div>
  );
}
