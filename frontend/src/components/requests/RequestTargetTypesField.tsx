import { Checkbox } from "@foundation/src/components/ui/checkbox";
import { Label } from "@foundation/src/components/ui/label";
import { resourceTypeIcon } from "@foundation/src/components/resources/resource-type-icon";
import type { ResourceTypeInfo } from "@foundation/src/lib/api/resource-types-api";

interface RequestTargetTypesFieldProps {
  resourceTypes: ResourceTypeInfo[];
  selectedKeys: string[];
  onChange: (keys: string[]) => void;
  readOnly: boolean;
}

/**
 * Which resource types this request needs. Drives the pickers below it, so it leads the
 * Resources tab. Clearing every type is allowed and meaningful — a request that needs no
 * resource, and so is never "scheduled".
 */
export function RequestTargetTypesField({
  resourceTypes,
  selectedKeys,
  onChange,
  readOnly,
}: RequestTargetTypesFieldProps) {
  const toggle = (key: string, checked: boolean) => {
    // Rebuilt from the type list rather than appended, so the order stays stable and
    // matches the order the pickers render in.
    const next = new Set(selectedKeys);
    if (checked) next.add(key);
    else next.delete(key);
    onChange(resourceTypes.filter((t) => next.has(t.key)).map((t) => t.key));
  };

  return (
    <div>
      <h4 className="text-sm font-medium">Needs</h4>
      <div className="flex flex-wrap gap-x-6 gap-y-3 pt-4">
        {resourceTypes.map((type) => {
          const Icon = resourceTypeIcon(type.icon);
          return (
            <div key={type.key} className="flex items-center gap-2">
              <Checkbox
                id={`target-type-${type.key}`}
                checked={selectedKeys.includes(type.key)}
                onCheckedChange={(checked) => toggle(type.key, !!checked)}
                disabled={readOnly}
              />
              <Label htmlFor={`target-type-${type.key}`} className="text-sm cursor-pointer flex items-center gap-1.5">
                <Icon className="h-4 w-4" />
                {type.displayName}
              </Label>
            </div>
          );
        })}
      </div>
    </div>
  );
}
