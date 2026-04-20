import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Switch } from "@/components/ui/switch";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { RotateCcw, Loader2, Info } from "lucide-react";
import type { TenantSettingDescriptor } from "@/lib/api/tenant-settings-api";
import { isModified, formatRange, isColorSetting, validate } from "./tenant-config-helpers";

interface SettingRowProps {
  descriptor: TenantSettingDescriptor;
  editValue: string;
  onChange: (key: string, value: string) => void;
  onReset: (key: string) => void;
  isResetting: boolean;
}

export function SettingRow({
  descriptor,
  editValue,
  onChange,
  onReset,
  isResetting,
}: SettingRowProps) {
  const modified = isModified(descriptor);
  const error = validate(descriptor, editValue);
  const range = formatRange(descriptor);
  const hasLocalChange = editValue !== descriptor.currentValue;

  return (
    <div className="grid grid-cols-[1fr_auto] gap-4 items-start py-3">
      <div className="space-y-1.5">
        <div className="flex items-center gap-2">
          <Label htmlFor={descriptor.key} className="font-medium text-sm">
            {descriptor.displayName}
          </Label>
          {modified && (
            <Badge variant="outline" className="text-xs px-1.5 py-0">
              modified
            </Badge>
          )}
          <TooltipProvider delayDuration={300}>
            <Tooltip>
              <TooltipTrigger asChild>
                <Info className="h-3.5 w-3.5 text-muted-foreground cursor-help" />
              </TooltipTrigger>
              <TooltipContent side="top" className="max-w-xs">
                <p>{descriptor.description}</p>
                {range && (
                  <p className="text-muted-foreground mt-1">
                    Range: {range}
                  </p>
                )}
                <p className="text-muted-foreground mt-1">
                  Default: {descriptor.defaultValue}
                </p>
              </TooltipContent>
            </Tooltip>
          </TooltipProvider>
        </div>
        <p className="text-xs text-muted-foreground">{descriptor.description}</p>
      </div>

      <div className="flex items-center gap-2">
        <div className="relative">
          {descriptor.valueType === "bool" ? (
            <Switch
              id={descriptor.key}
              checked={editValue === "True" || editValue === "true"}
              onCheckedChange={(checked) =>
                onChange(descriptor.key, checked ? "True" : "False")
              }
            />
          ) : isColorSetting(descriptor) ? (
            <div className="flex items-center gap-2">
              <input
                type="color"
                value={/^#[0-9a-fA-F]{6}$/.test(editValue) ? editValue : descriptor.defaultValue}
                onChange={(e) => onChange(descriptor.key, e.target.value)}
                className="h-9 w-9 cursor-pointer rounded border border-input p-0.5"
              />
              <Input
                id={descriptor.key}
                value={editValue}
                onChange={(e) => onChange(descriptor.key, e.target.value)}
                className={`w-32 text-sm font-mono ${error ? "border-destructive" : ""} ${hasLocalChange ? "border-primary" : ""}`}
                type="text"
                placeholder="#000000"
                maxLength={7}
              />
            </div>
          ) : (
            <Input
              id={descriptor.key}
              value={editValue}
              onChange={(e) => onChange(descriptor.key, e.target.value)}
              className={`w-48 text-sm ${error ? "border-destructive" : ""} ${hasLocalChange ? "border-primary" : ""}`}
              type="text"
              inputMode={
                descriptor.valueType === "int"
                  ? "numeric"
                  : descriptor.valueType === "double"
                    ? "decimal"
                    : "text"
              }
            />
          )}
          {error && (
            <p className="text-xs text-destructive mt-1 absolute">{error}</p>
          )}
        </div>

        {modified && (
          <TooltipProvider delayDuration={300}>
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-8 w-8 text-muted-foreground hover:text-foreground"
                  onClick={() => onReset(descriptor.key)}
                  disabled={isResetting}
                >
                  {isResetting ? (
                    <Loader2 className="h-4 w-4 animate-spin" />
                  ) : (
                    <RotateCcw className="h-4 w-4" />
                  )}
                </Button>
              </TooltipTrigger>
              <TooltipContent>Reset to default ({descriptor.defaultValue})</TooltipContent>
            </Tooltip>
          </TooltipProvider>
        )}
      </div>
    </div>
  );
}
