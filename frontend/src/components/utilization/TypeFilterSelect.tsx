import { useMemo } from 'react';
import { CheckableFilterMenu } from '@foundation/src/components/ui/CheckableFilterMenu';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';

interface TypeFilterSelectProps {
  available: readonly ResourceTypeInfo[];
  selected: readonly string[];
  onChange: (keys: string[]) => void;
}

/** Which resource types a grid tab shows. The interaction is the shared filter menu. */
export function TypeFilterSelect({ available, selected, onChange }: TypeFilterSelectProps) {
  const items = useMemo(
    () => available.map((t) => ({ value: t.key, label: t.displayNamePlural })),
    [available],
  );

  return (
    <CheckableFilterMenu
      items={items}
      selected={selected}
      onChange={onChange}
      allLabel="All types"
      noun="types"
      ariaLabel="Filter by type"
    />
  );
}
