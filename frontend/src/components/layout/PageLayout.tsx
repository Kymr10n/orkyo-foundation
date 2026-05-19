import type { ReactNode } from "react";
import { cn } from "@foundation/src/lib/utils";

interface PageLayoutProps {
  children: ReactNode;
  className?: string;
}

export function PageLayout({ children, className }: PageLayoutProps) {
  return (
    <div className={cn("flex flex-col h-full p-4 md:p-6 lg:p-8", className)}>
      {children}
    </div>
  );
}
