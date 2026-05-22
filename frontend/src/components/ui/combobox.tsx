import { useMemo, useState, useEffect, useRef } from "react";
import { Check, ChevronsUpDown, Search } from "lucide-react";

import { cn } from "@foundation/src/lib/utils";
import { Button } from "@foundation/src/components/ui/button";
import { Input } from "@foundation/src/components/ui/input";
import { Popover, PopoverContent, PopoverTrigger } from "@foundation/src/components/ui/popover";

export interface ComboboxOption {
  id: string;
  label: string;
}

interface ComboboxProps {
  value: string;
  onChange: (id: string) => void;
  options: ComboboxOption[];
  placeholder?: string;
  searchPlaceholder?: string;
  emptyText?: string;
  disabled?: boolean;
  id?: string;
  className?: string;
  /** Maximum height of the list area before scrolling. */
  maxListHeightPx?: number;
}

/**
 * Searchable single-select. Lightweight Popover + filtered list — no extra
 * deps. Suitable for option lists up to ~1k items; switch to a virtualized
 * implementation if a single picker regularly exceeds that.
 */
export function Combobox({
  value,
  onChange,
  options,
  placeholder = "Select…",
  searchPlaceholder = "Search…",
  emptyText = "No matches",
  disabled,
  id,
  className,
  maxListHeightPx = 240,
}: ComboboxProps) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (open) {
      setQuery("");
      // Focus the search field once the popover mounts.
      const t = setTimeout(() => inputRef.current?.focus(), 0);
      return () => clearTimeout(t);
    }
  }, [open]);

  const selected = options.find((o) => o.id === value);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return options;
    return options.filter((o) => o.label.toLowerCase().includes(q));
  }, [options, query]);

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          id={id}
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          disabled={disabled}
          className={cn(
            "w-full justify-between font-normal",
            !selected && "text-muted-foreground",
            className,
          )}
        >
          <span className="truncate">{selected?.label ?? placeholder}</span>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        className="p-0 w-[--radix-popover-trigger-width] min-w-[14rem]"
        align="start"
      >
        <div className="flex items-center border-b px-2">
          <Search className="h-4 w-4 text-muted-foreground shrink-0" />
          <Input
            ref={inputRef}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={searchPlaceholder}
            className="h-9 border-0 shadow-none focus-visible:ring-0 focus-visible:ring-offset-0 px-2"
          />
        </div>
        <div
          className="overflow-y-auto py-1"
          style={{ maxHeight: maxListHeightPx }}
          role="listbox"
        >
          {filtered.length === 0 ? (
            <div className="px-3 py-2 text-xs text-muted-foreground">{emptyText}</div>
          ) : (
            filtered.map((o) => {
              const isSelected = o.id === value;
              return (
                <button
                  key={o.id}
                  type="button"
                  role="option"
                  aria-selected={isSelected}
                  onClick={() => {
                    onChange(o.id);
                    setOpen(false);
                  }}
                  className={cn(
                    "flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm",
                    "hover:bg-accent hover:text-accent-foreground",
                    "focus:bg-accent focus:text-accent-foreground focus:outline-none",
                    isSelected && "bg-accent/50",
                  )}
                >
                  <Check
                    className={cn(
                      "h-4 w-4 shrink-0",
                      isSelected ? "opacity-100" : "opacity-0",
                    )}
                  />
                  <span className="truncate">{o.label}</span>
                </button>
              );
            })
          )}
        </div>
      </PopoverContent>
    </Popover>
  );
}
