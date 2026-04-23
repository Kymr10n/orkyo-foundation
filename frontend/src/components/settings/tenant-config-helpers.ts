import {
  Shield,
  Palette,
  Upload,
  Search,
  Mail,
  Calendar,
} from "lucide-react";
import type { TenantSettingDescriptor } from "@foundation/src/lib/api/tenant-settings-api";

export const CATEGORY_META: Record<
  string,
  { label: string; description: string; icon: React.ElementType }
> = {
  security: {
    label: "Security",
    description: "Authentication and access-control settings",
    icon: Shield,
  },
  invitations: {
    label: "Invitations",
    description: "User invitation and onboarding settings",
    icon: Mail,
  },
  uploads: {
    label: "Uploads",
    description: "File upload size limits and allowed file types",
    icon: Upload,
  },
  search: {
    label: "Search",
    description: "Search behavior and similarity matching thresholds",
    icon: Search,
  },
  branding: {
    label: "Branding",
    description: "Customize product name and theme colors for your org",
    icon: Palette,
  },
  scheduling: {
    label: "Scheduling",
    description: "Auto-scheduling feature controls",
    icon: Calendar,
  },
};

export function isModified(descriptor: TenantSettingDescriptor): boolean {
  return descriptor.currentValue !== descriptor.defaultValue;
}

export function formatRange(d: TenantSettingDescriptor): string | null {
  if (d.minValue != null && d.maxValue != null)
    return `${d.minValue} – ${d.maxValue}`;
  if (d.minValue != null) return `≥ ${d.minValue}`;
  if (d.maxValue != null) return `≤ ${d.maxValue}`;
  return null;
}

export function isColorSetting(d: TenantSettingDescriptor): boolean {
  return d.valueType === "string" && d.key.endsWith("_color");
}

export function validate(
  d: TenantSettingDescriptor,
  value: string,
): string | null {
  if (d.valueType === "int") {
    if (!/^-?\d+$/.test(value)) return "Must be a whole number";
    const n = parseInt(value, 10);
    if (d.minValue != null && n < parseInt(d.minValue, 10))
      return `Minimum is ${d.minValue}`;
    if (d.maxValue != null && n > parseInt(d.maxValue, 10))
      return `Maximum is ${d.maxValue}`;
  }
  if (d.valueType === "double") {
    if (isNaN(parseFloat(value))) return "Must be a number";
    const n = parseFloat(value);
    if (d.minValue != null && n < parseFloat(d.minValue))
      return `Minimum is ${d.minValue}`;
    if (d.maxValue != null && n > parseFloat(d.maxValue))
      return `Maximum is ${d.maxValue}`;
  }
  if (d.valueType === "string" && value.trim().length === 0) {
    return "Cannot be empty";
  }
  return null;
}
