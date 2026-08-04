import type { ReactNode } from "react";

interface SettingsPageHeaderProps {
  title: string;
  description: ReactNode;
  children?: ReactNode;
}

export function SettingsPageHeader({ title, description, children }: SettingsPageHeaderProps) {
  // gap-4 + min-w-0 keep the description from running under the action button on a phone:
  // the text column may shrink and wrap, the actions may not (Button is whitespace-nowrap,
  // so shrink-0 is what it already wants).
  return (
    <div className="flex items-start justify-between gap-4">
      <div className="min-w-0">
        <h2 className="text-lg font-semibold">{title}</h2>
        <p className="text-sm text-muted-foreground mt-1">{description}</p>
      </div>
      {children && <div className="flex shrink-0 items-center gap-3">{children}</div>}
    </div>
  );
}
