import { Button } from "@foundation/src/components/ui/button";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@foundation/src/components/ui/popover";
import {
  REQUEST_ICONS,
  REQUEST_ICON_GROUPS,
  getRequestIcon,
} from "@foundation/src/constants/requestIcons";
import { cn } from "@foundation/src/lib/utils";
import { Circle, X } from "lucide-react";
import { useState } from "react";

interface RequestIconSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  disabled?: boolean;
  id?: string;
}

export function RequestIconSelector({
  value,
  onChange,
  disabled,
  id,
}: RequestIconSelectorProps) {
  const [open, setOpen] = useState(false);
  const Current = getRequestIcon(value);

  function pick(nextId: string | null) {
    onChange(nextId);
    setOpen(false);
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          id={id}
          type="button"
          variant="outline"
          size="icon"
          disabled={disabled}
          aria-label={value ? `Icon: ${value}` : "Pick an icon"}
          className="h-9 w-9"
        >
          {Current ? <Current className="h-4 w-4" /> : <Circle className="h-4 w-4 text-muted-foreground" />}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-[280px] p-3" align="start">
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-sm font-medium">Choose an icon</span>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="h-7 gap-1 px-2 text-xs"
              onClick={() => pick(null)}
            >
              <X className="h-3 w-3" /> None
            </Button>
          </div>
          {REQUEST_ICON_GROUPS.map((group) => (
            <div key={group} className="space-y-1.5">
              <div className="text-xs font-medium text-muted-foreground">{group}</div>
              <div className="grid grid-cols-6 gap-1">
                {REQUEST_ICONS.filter((icon) => icon.group === group).map((icon) => {
                  const Icon = icon.component;
                  const selected = icon.id === value;
                  return (
                    <button
                      key={icon.id}
                      type="button"
                      onClick={() => pick(icon.id)}
                      title={icon.label}
                      aria-label={icon.label}
                      aria-pressed={selected}
                      className={cn(
                        "flex h-8 w-8 items-center justify-center rounded border border-transparent text-foreground transition-colors hover:bg-accent hover:text-accent-foreground",
                        selected && "border-primary bg-accent text-accent-foreground",
                      )}
                    >
                      <Icon className="h-4 w-4" />
                    </button>
                  );
                })}
              </div>
            </div>
          ))}
        </div>
      </PopoverContent>
    </Popover>
  );
}
