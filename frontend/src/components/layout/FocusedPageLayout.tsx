import type { ReactNode } from "react";
import { cn } from "@foundation/src/lib/utils";

interface FocusedPageLayoutProps {
  children: ReactNode;
  className?: string;
}

/**
 * Page shell for the standalone, cross-tenant pages that render OUTSIDE the
 * main AppLayout (no sidebar) — e.g. Account and Messages. It mirrors the
 * app's content rhythm (the same responsive padding as {@link PageLayout})
 * inside a full-height, centered, readable column, so these pages read as the
 * same product as the sidebar'd pages without depending on tenant chrome.
 */
export function FocusedPageLayout({ children, className }: FocusedPageLayoutProps) {
  return (
    <div className="min-h-screen">
      <div className={cn("mx-auto max-w-4xl p-4 md:p-6 lg:p-8 space-y-6", className)}>
        {children}
      </div>
    </div>
  );
}
