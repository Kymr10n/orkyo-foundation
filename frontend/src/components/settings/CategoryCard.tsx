import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@foundation/src/components/ui/card";
import { Settings2 } from "lucide-react";
import type { TenantSettingDescriptor } from "@foundation/src/lib/api/tenant-settings-api";
import { SettingRow } from "./SettingRow";
import { CATEGORY_META } from "./tenant-config-helpers";

interface CategoryCardProps {
  category: string;
  settings: TenantSettingDescriptor[];
  editValues: Record<string, string>;
  onChange: (key: string, value: string) => void;
  onReset: (key: string) => void;
  resettingKey: string | null;
}

export function CategoryCard({
  category,
  settings,
  editValues,
  onChange,
  onReset,
  resettingKey,
}: CategoryCardProps) {
  const meta = CATEGORY_META[category] ?? {
    label: category,
    description: "",
    icon: Settings2,
  };
  const Icon = meta.icon;

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center gap-2 text-base">
          <Icon className="h-4 w-4" />
          {meta.label}
        </CardTitle>
        <CardDescription>{meta.description}</CardDescription>
      </CardHeader>
      <CardContent>
        <div className="divide-y">
          {settings.map((s) => (
            <SettingRow
              key={s.key}
              descriptor={s}
              editValue={editValues[s.key] ?? s.currentValue}
              onChange={onChange}
              onReset={onReset}
              isResetting={resettingKey === s.key}
            />
          ))}
        </div>
      </CardContent>
    </Card>
  );
}
