import type { ReactNode } from "react";

export function PageLayout({ children }: { children: ReactNode }) {
  return <div className="flex flex-col h-full p-4 md:p-6 lg:p-8">{children}</div>;
}
