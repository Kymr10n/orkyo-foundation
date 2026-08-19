import { Check, ListFilter } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@foundation/src/components/ui/dropdown-menu';

export interface CheckableFilterItem {
  value: string;
  label: string;
  /** Optional swatch rendered before the label, so a menu can echo a legend. */
  swatchClassName?: string;
}

interface CheckableFilterMenuProps {
  items: readonly CheckableFilterItem[];
  selected: readonly string[];
  onChange: (values: string[]) => void;
  /** Shown on the trigger, and as the reset entry, when everything is selected. */
  allLabel: string;
  /** Pluralised in the trigger's count: "2 statuses". */
  noun: string;
  ariaLabel: string;
}

/**
 * A menu of checkable options that reads as one filter.
 *
 * A menu rather than a Select because the choice is a set, and a Select would claim to hold one
 * value. The trigger always says what is on, so a short list never looks arbitrarily short with
 * the reason off screen, and selecting nothing means everything — a filter that can hide every
 * row is a way to look at an empty view and think the data is gone.
 */
export function CheckableFilterMenu({
  items,
  selected,
  onChange,
  allLabel,
  noun,
  ariaLabel,
}: CheckableFilterMenuProps) {
  const allSelected = selected.length === items.length;
  const label = allSelected
    ? allLabel
    : selected.length === 1
      ? (items.find((i) => i.value === selected[0])?.label ?? `1 ${noun}`)
      : `${selected.length} ${noun}`;

  const toggle = (value: string) => {
    const next = selected.includes(value)
      ? selected.filter((v) => v !== value)
      : [...selected, value];
    onChange(next.length === 0 ? items.map((i) => i.value) : next);
  };

  // Nothing to choose between.
  if (items.length <= 1) return null;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" aria-label={ariaLabel}>
          <ListFilter className="mr-2 h-4 w-4" />
          {label}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel className="sr-only">{ariaLabel}</DropdownMenuLabel>
        <DropdownMenuItem
          onSelect={(e) => {
            e.preventDefault();
            onChange(items.map((i) => i.value));
          }}
        >
          <Check className={allSelected ? 'mr-2 h-4 w-4' : 'mr-2 h-4 w-4 opacity-0'} />
          {allLabel}
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        {items.map((item) => (
          <DropdownMenuItem
            key={item.value}
            // Keeping the menu open lets a set be built in one visit instead of one per click.
            onSelect={(e) => {
              e.preventDefault();
              toggle(item.value);
            }}
          >
            <Check
              className={selected.includes(item.value) ? 'mr-2 h-4 w-4' : 'mr-2 h-4 w-4 opacity-0'}
            />
            {item.swatchClassName && (
              <span className={`mr-2 h-3 w-3 rounded-sm border ${item.swatchClassName}`} aria-hidden />
            )}
            {item.label}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
