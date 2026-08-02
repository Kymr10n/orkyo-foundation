import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@foundation/src/components/ui/select";
import { useResourceTypes } from "@foundation/src/hooks/useResourceTypes";

interface Props {
  value: string;
  onChange: (resourceTypeKey: string) => void;
  disabled?: boolean;
}

/**
 * Which resource type an auto-schedule run fills. One run fills one type's slot, so the
 * user picks it up front and the same value is replayed on apply.
 *
 * Active types only: an inactive type is out of planning and has nothing to schedule onto.
 */
export function AutoScheduleTypeSelect({ value, onChange, disabled }: Props) {
  const { data: resourceTypes = [] } = useResourceTypes(true);

  return (
    <Select value={value} onValueChange={onChange} disabled={disabled}>
      <SelectTrigger className="w-[140px]" aria-label="Resource type to auto-schedule">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {resourceTypes.map((type) => (
          <SelectItem key={type.key} value={type.key}>
            {type.displayName}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
