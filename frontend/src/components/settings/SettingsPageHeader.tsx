import type { ReactNode } from "react";

interface SettingsPageHeaderProps {
  title: string;
  description: ReactNode;
  children?: ReactNode;
}

export function SettingsPageHeader({ title, description, children }: SettingsPageHeaderProps) {
  return (
    <div className="flex items-start justify-between">
      <div>
        <h2 className="text-lg font-semibold">{title}</h2>
        <p className="text-sm text-muted-foreground mt-1">{description}</p>
      </div>
      {children && <div className="flex items-center gap-3">{children}</div>}
    </div>
  );
}
