import type { ReactNode } from "react";
import { Tabs, TabsList, TabsTrigger } from "@foundation/src/components/ui/tabs";

export interface PageTab {
  value: string;
  label: string;
}

interface PageTabsProps {
  tabs: PageTab[];
  value: string;
  onChange: (value: string) => void;
  children: ReactNode;
}

export function PageTabs({ tabs, value, onChange, children }: PageTabsProps) {
  return (
    <Tabs value={value} onValueChange={onChange} className="flex-1 flex flex-col">
      <TabsList className="mb-4">
        {tabs.map((t) => (
          <TabsTrigger key={t.value} value={t.value}>
            {t.label}
          </TabsTrigger>
        ))}
      </TabsList>
      <div className="flex-1 min-h-0">{children}</div>
    </Tabs>
  );
}
