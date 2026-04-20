/**
 * StarterTemplatePicker Component
 *
 * Shown during onboarding to let the user choose how to initialise their
 * new organisation database. Supports five templates:
 *   • Empty          – blank slate
 *   • Demo           – full sample data with floorplan
 *   • Camping Site   – industry preset
 *   • Construction   – industry preset
 *   • Manufacturing  – industry preset
 */

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import {
  FilePlus,
  LayoutDashboard,
  Tent,
  HardHat,
  Factory,
  type LucideIcon,
} from "lucide-react";
import { cn } from "@/lib/utils";

export interface StarterTemplate {
  key: string;
  name: string;
  description: string;
  icon: string;
  includesDemoData: boolean;
}

interface StarterTemplatePickerProps {
  templates: StarterTemplate[];
  selected: string;
  onSelect: (key: string) => void;
  disabled?: boolean;
}

/** Maps the icon name returned by the API to a Lucide component. */
const ICON_MAP: Record<string, LucideIcon> = {
  "file-plus": FilePlus,
  "layout-dashboard": LayoutDashboard,
  tent: Tent,
  "hard-hat": HardHat,
  factory: Factory,
};

export function StarterTemplatePicker({
  templates,
  selected,
  onSelect,
  disabled,
}: StarterTemplatePickerProps) {
  return (
    <div className="space-y-3">
      <p className="text-sm text-muted-foreground">
        Choose a starting point for your workspace:
      </p>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
        {templates.map((t) => {
          const Icon = ICON_MAP[t.icon] ?? FilePlus;
          const isSelected = selected === t.key;

          return (
            <Card
              key={t.key}
              role="radio"
              aria-checked={isSelected}
              tabIndex={disabled ? -1 : 0}
              className={cn(
                "cursor-pointer transition-all select-none",
                isSelected
                  ? "border-primary ring-2 ring-primary/20"
                  : "hover:border-muted-foreground/40",
                disabled && "opacity-50 pointer-events-none"
              )}
              onClick={() => !disabled && onSelect(t.key)}
              onKeyDown={(e) => {
                if (!disabled && (e.key === "Enter" || e.key === " ")) {
                  e.preventDefault();
                  onSelect(t.key);
                }
              }}
            >
              <CardHeader className="pb-2 pt-4 px-4">
                <div className="flex items-center gap-2">
                  <Icon className="h-5 w-5 text-primary shrink-0" />
                  <CardTitle className="text-sm font-semibold">{t.name}</CardTitle>
                  {t.includesDemoData && (
                    <Badge variant="secondary" className="text-[10px] px-1.5 py-0">
                      sample data
                    </Badge>
                  )}
                </div>
              </CardHeader>
              <CardContent className="px-4 pb-4 pt-0">
                <CardDescription className="text-xs leading-relaxed">
                  {t.description}
                </CardDescription>
              </CardContent>
            </Card>
          );
        })}
      </div>
    </div>
  );
}
