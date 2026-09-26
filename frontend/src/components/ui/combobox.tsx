import { useMemo, useState, useEffect, useRef, type ReactNode } from "react";
import { Check, ChevronsUpDown, Search } from "lucide-react";

import { cn } from "@foundation/src/lib/utils";
import { Button } from "@foundation/src/components/ui/button";
import { Input } from "@foundation/src/components/ui/input";
import { Popover, PopoverContent, PopoverTrigger } from "@foundation/src/components/ui/popover";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@foundation/src/components/ui/sheet";
import { VisuallyHidden } from "@foundation/src/components/ui/visually-hidden";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";

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
  /** Maximum height of the list area before scrolling. Popover mode only. */
  maxListHeightPx?: number;
  /**
   * Cap on how many matches are rendered at once. Unset renders them all, which is the right
   * default for the small lists most pickers hold. Set it where the source list can run to
   * thousands: rendering every match then costs more than the user can read anyway, and the
   * search box is the way through it.
   */
  maxResults?: number;
}

/**
 * Searchable single-select. Lightweight Popover + filtered list — no extra
 * deps. Suitable for option lists up to ~1k items; switch to a virtualized
 * implementation if a single picker regularly exceeds that.
 *
 * On a phone it opens as a full-screen sheet instead of a popover. An anchored popover
 * with an auto-focused search box is the right shape for a pointer and a keyboard, and
 * the wrong one for a phone: the software keyboard takes most of the screen and the
 * popover is squeezed into what is left, so the list cannot be browsed. The sheet gives
 * the list the whole screen and brings the keyboard up only when the search box is tapped.
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
  maxResults,
}: ComboboxProps) {
  const { isPhone } = useBreakpoint();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);
  const sheetRef = useRef<HTMLDivElement>(null);

  // Clearing the query is a render-phase update; focusing is a real side effect and stays
  // in an effect below.
  const [syncedOpen, setSyncedOpen] = useState(open);
  if (syncedOpen !== open) {
    setSyncedOpen(open);
    if (open) setQuery("");
  }

  useEffect(() => {
    // Popover only: on a phone, focusing would raise the keyboard over the list.
    if (!open || isPhone) return;
    // Focus the search field once the popover mounts.
    const t = setTimeout(() => inputRef.current?.focus(), 0);
    return () => clearTimeout(t);
  }, [open, isPhone]);

  const selected = options.find((o) => o.id === value);

  const matches = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return options;
    return options.filter((o) => o.label.toLowerCase().includes(q));
  }, [options, query]);

  const filtered = useMemo(
    () => (maxResults != null && matches.length > maxResults ? matches.slice(0, maxResults) : matches),
    [matches, maxResults],
  );
  const truncated = matches.length - filtered.length;

  const choose = (optionId: string) => {
    onChange(optionId);
    setOpen(false);
  };

  const trigger = (
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
  );

  const searchField = (
    <div className="flex items-center border-b px-2">
      <Search className="h-4 w-4 text-muted-foreground shrink-0" />
      <Input
        ref={inputRef}
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder={searchPlaceholder}
        className={cn(
          "border-0 shadow-none focus-visible:ring-0 focus-visible:ring-offset-0 px-2",
          isPhone ? "h-12 text-base" : "h-9",
        )}
      />
    </div>
  );

  const list = (className: string, style?: React.CSSProperties): ReactNode => (
    <div className={cn("py-1", className)} style={style} role="listbox">
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
              onClick={() => choose(o.id)}
              className={cn(
                "flex w-full items-center gap-2 px-3 text-left",
                // Touch rows are taller and read at body size; pointer rows stay compact.
                isPhone ? "py-3 text-base" : "py-1.5 text-sm",
                "hover:bg-accent hover:text-accent-foreground",
                "focus:bg-accent focus:text-accent-foreground focus:outline-hidden",
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
  );

  // Outside the listbox: it is a status message, not an option, and a non-option child
  // of role="listbox" is invalid. Saying how many are hidden turns "the list looks
  // wrong" into "type a bit more".
  const truncatedNote = truncated > 0 && (
    <div role="status" className="border-t px-3 py-2 text-xs text-muted-foreground">
      {truncated} more match{truncated === 1 ? "" : "es"} — refine your search
    </div>
  );

  if (isPhone) {
    return (
      <Sheet open={open} onOpenChange={setOpen}>
        <SheetTrigger asChild>{trigger}</SheetTrigger>
        {/* Full screen, like DialogContent on a phone: the list gets every pixel the
            keyboard leaves, and the header keeps the picker's purpose in view. */}
        <SheetContent
          ref={sheetRef}
          side="bottom"
          className="h-[100dvh] max-h-[100dvh] gap-0 rounded-none p-0"
          // Radix focuses the first tabbable on open, which is the search box, and a
          // focused text field raises the keyboard. Park focus on the sheet itself.
          onOpenAutoFocus={(e) => {
            e.preventDefault();
            sheetRef.current?.focus();
          }}
        >
          <SheetHeader className="border-b px-4 py-3 text-left">
            <SheetTitle className="pr-8 text-base">{placeholder}</SheetTitle>
            <VisuallyHidden>
              <SheetDescription>Search, or scroll the list.</SheetDescription>
            </VisuallyHidden>
          </SheetHeader>
          {searchField}
          {list("min-h-0 flex-1 overflow-y-auto")}
          {truncatedNote}
        </SheetContent>
      </Sheet>
    );
  }

  // `modal`, so the list scrolls inside a dialog. A modal Dialog wraps its content in
  // react-remove-scroll, which cancels touch and wheel scrolling on everything outside it —
  // and this popover is portalled outside it. A modal popover mounts its own scroll lock, and
  // the most recent lock wins, so the listbox becomes the scrollable region again. Without
  // this, the list could only be filtered on a tablet, never scrolled. Select does the same.
  return (
    <Popover modal open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>{trigger}</PopoverTrigger>
      <PopoverContent
        className="p-0 w-[--radix-popover-trigger-width] min-w-[14rem]"
        align="start"
      >
        {searchField}
        {list("overflow-y-auto", { maxHeight: maxListHeightPx })}
        {truncatedNote}
      </PopoverContent>
    </Popover>
  );
}
