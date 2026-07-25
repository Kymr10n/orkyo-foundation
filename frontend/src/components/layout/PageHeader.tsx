import type { ReactNode } from "react";
import { cn } from "@foundation/src/lib/utils";

interface PageHeaderProps {
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  className?: string;
}

export function PageHeader({ title, description, actions, className }: PageHeaderProps) {
  return (
    <div className={cn("flex items-center justify-between mb-4 md:mb-6", className)}>
      <div>
        <h1 className="text-xl md:text-2xl font-bold">{title}</h1>
        {/* Subheading is explanatory, not essential — hidden on phones (< md) to
            reclaim vertical space; shown from tablet up. */}
        {description && <p className="hidden md:block text-sm text-muted-foreground">{description}</p>}
      </div>
      {actions && <div className="flex items-center gap-2">{actions}</div>}
    </div>
  );
}
