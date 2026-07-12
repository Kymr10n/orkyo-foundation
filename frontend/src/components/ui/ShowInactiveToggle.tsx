/**
 * Shared "Show inactive" filter checkbox used by reference-data settings lists
 * (job titles, departments). Extracted from the duplicated inline blocks (W2.5);
 * keeps the native checkbox markup so visuals are unchanged.
 */
interface ShowInactiveToggleProps {
  /** Unique element id so the label associates correctly per list. */
  id: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}

export function ShowInactiveToggle({ id, checked, onChange }: ShowInactiveToggleProps) {
  return (
    <div className="flex items-center gap-2">
      <input
        id={id}
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
      />
      <label htmlFor={id} className="text-sm text-muted-foreground">
        Show inactive
      </label>
    </div>
  );
}
