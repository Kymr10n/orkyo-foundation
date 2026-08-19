import { useMemo } from 'react';
import {
  SharedListRowsPanel,
  type SharedListEntry,
} from '@foundation/src/components/lists/SharedListRowsPanel';
import { useResourceCustomFields } from '@foundation/src/hooks/useResourceCustomFields';
import { useResourceTypeTabContext } from './resourceTypeTabContext';

/**
 * The shared catalogues one resource type uses — a mill's tooling, a person's job titles.
 *
 * Keyed on what the type's `list_lookup` fields bind, not on which definitions the type owns. A
 * definition can serve several types (the demo's Tooling Catalog serves mills and drills), so
 * ownership alone would hide exactly the lists a reader came here for.
 *
 * Per-resource lists are deliberately absent: their rows belong to one resource and are edited on
 * that resource, where the question "whose maintenance log is this?" has an answer.
 */
export function ResourceListsTab() {
  const { resourceType } = useResourceTypeTabContext();
  const { data: fields = [] } = useResourceCustomFields(resourceType.id);

  const entries = useMemo<SharedListEntry[]>(
    () =>
      fields
        // The instance is what this tab edits, so a field without one has nothing to show.
        .filter((f) => f.isActive && f.dataType === 'list_lookup' && f.listInstanceId)
        // A lookup field names its instance, never its definition — the binding CHECK in
        // migration 1780 forbids carrying both. The panel resolves the definition from it.
        .map((f) => ({ id: f.id, label: f.label, instanceId: f.listInstanceId })),
    [fields],
  );

  return (
    <SharedListRowsPanel
      entries={entries}
      selectId="resource-list"
      emptyMessage={`${resourceType.displayNamePlural} use no shared lists yet. An administrator binds one through a custom field under Configuration.`}
    />
  );
}
