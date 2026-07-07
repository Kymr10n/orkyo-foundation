import type { LucideIcon } from "lucide-react";
import { MoreHorizontal } from "lucide-react";
import { Button } from "@foundation/src/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@foundation/src/components/ui/dropdown-menu";

export interface RowAction {
  /** Visible label — the menu is always labelled, unlike a bare icon strip. */
  label: string;
  icon: LucideIcon;
  onSelect: () => void;
  disabled?: boolean;
  /** Render in the destructive (red) style and place below a separator. */
  destructive?: boolean;
}

interface RowActionsProps {
  actions: RowAction[];
  /** Accessible label for the trigger, e.g. `Actions for ${name}`. */
  triggerLabel?: string;
}

/**
 * Standard per-row action affordance: a labelled kebab menu instead of an
 * unlabelled icon strip. Destructive actions are grouped below a separator so a
 * Delete sits apart from benign edits. Mirrors the pattern in RequestListView.
 *
 * The wrapper stops click propagation so opening the menu (or invoking an
 * action) never also triggers a row/card onClick.
 */
export function RowActions({ actions, triggerLabel = "Row actions" }: RowActionsProps) {
  const benign = actions.filter((a) => !a.destructive);
  const destructive = actions.filter((a) => a.destructive);

  return (
    <div className="flex justify-end" onClick={(e) => e.stopPropagation()}>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="icon-sm" aria-label={triggerLabel}>
            <MoreHorizontal className="h-4 w-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          {benign.map(({ label, icon: Icon, onSelect, disabled }) => (
            <DropdownMenuItem key={label} onClick={onSelect} disabled={disabled}>
              <Icon className="h-4 w-4 mr-2" />
              {label}
            </DropdownMenuItem>
          ))}
          {destructive.length > 0 && benign.length > 0 && <DropdownMenuSeparator />}
          {destructive.map(({ label, icon: Icon, onSelect, disabled }) => (
            <DropdownMenuItem
              key={label}
              className="text-destructive focus:text-destructive"
              onClick={onSelect}
              disabled={disabled}
            >
              <Icon className="h-4 w-4 mr-2" />
              {label}
            </DropdownMenuItem>
          ))}
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  );
}
