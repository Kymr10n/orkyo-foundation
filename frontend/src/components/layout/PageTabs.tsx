import type { ReactNode } from "react";
import { Tabs, TabsList, TabsTrigger } from "@foundation/src/components/ui/tabs";
import { cn } from "@foundation/src/lib/utils";

export interface PageTab {
  value: string;
  label: string;
}

interface PageTabsProps {
  tabs: PageTab[];
  value: string;
  onChange: (value: string) => void;
  /** Optional controls rendered as a row between the tab strip and the content. */
  toolbar?: ReactNode;
  children: ReactNode;
  className?: string;
}

export function PageTabs({ tabs, value, onChange, toolbar, children, className }: PageTabsProps) {
  return (
    <Tabs value={value} onValueChange={onChange} className={cn("flex-1 flex flex-col", className)}>
      <TabsList className="mb-4 w-full">
        {tabs.map((t) => (
          <TabsTrigger key={t.value} value={t.value}>
            {t.label}
          </TabsTrigger>
        ))}
      </TabsList>
      {toolbar && <div className="flex flex-wrap items-center gap-2 mb-3">{toolbar}</div>}
      <div className="flex-1 min-h-0">{children}</div>
    </Tabs>
  );
}
