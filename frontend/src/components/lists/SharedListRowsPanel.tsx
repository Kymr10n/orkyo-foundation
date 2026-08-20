import { useState } from 'react';
import { Rows3 } from 'lucide-react';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import { EmptyState } from '@foundation/src/components/ui/EmptyState';
import { ListRowsEditor } from '@foundation/src/components/lists/ListRowsEditor';
import { useListDefinition, useSharedListInstances } from '@foundation/src/hooks/useListDefinitions';
import { useListInstance } from '@foundation/src/hooks/useListRows';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';

/**
 * One selectable list. A caller supplies whichever half it holds: the Organization page knows the
 * definition and the resource Lists tab knows the instance (a `list_lookup` field carries an
 * instance id and, by the binding CHECK in migration 1780, never a definition id). The panel
 * resolves the other half.
 */
export interface SharedListEntry {
  /** Stable id for the selector — a definition id or a custom-field id, whatever the caller keys on. */
  id: string;
  label: string;
  definitionId?: string | null;
  /** The shared instance holding the rows. */
  instanceId?: string | null;
}

/**
 * "Departments" → "Department", "Job Titles" → "Job Title", "Tooling" → "Tooling".
 *
 * A heuristic, and English-only — the app has no i18n layer, and list names are tenant-authored.
 * It is deliberately conservative: anything it does not recognise is left alone, so the worst case
 * is a button reading "Add Tooling" rather than a wrong word.
 */
function singularize(label: string): string {
  if (/ies$/i.test(label)) return label.replace(/ies$/i, 'y');
  if (/(ss|us|is)$/i.test(label)) return label;
  return label.replace(/s$/, '');
}

interface SharedListRowsPanelProps {
  entries: readonly SharedListEntry[];
  /** Input id for the selector, so the two callers do not collide on one label association. */
  selectId: string;
  /** Shown when `entries` is empty — the callers explain absence differently. */
  emptyMessage: string;
}

/**
 * Pick a shared list, edit its rows. The surface behind both the Organization page and a resource
 * type's Lists tab: one list at a time, chosen from a selector, rendered through
 * {@link ListRowsEditor}.
 *
 * The two callers differ only in where their entries come from — organization-scoped definitions
 * on one side, a type's lookup fields on the other — so that difference is the prop and everything
 * below it is shared.
 */
export function SharedListRowsPanel({ entries, selectId, emptyMessage }: SharedListRowsPanelProps) {
  const canEdit = useCanEdit();
  const [selectedId, setSelectedId] = useState<string | null>(null);

  // Falls back to the first entry so the panel always shows something to edit rather than an
  // empty frame the reader has to act on first.
  const active = entries.find((e) => e.id === selectedId) ?? entries[0] ?? null;

  // Fill in whichever half the caller did not supply. Both queries sit idle when it did.
  const { data: instances = [] } = useSharedListInstances(
    active?.instanceId ? null : (active?.definitionId ?? null),
  );
  const instanceId = active?.instanceId ?? instances[0]?.id ?? null;

  const { data: instance } = useListInstance(active?.definitionId ? null : instanceId);
  const definitionId = active?.definitionId ?? instance?.listDefinitionId ?? null;

  const { data: definition } = useListDefinition(definitionId);
  const columns = (definition?.columns ?? []).filter((c) => c.isActive);

  if (entries.length === 0) {
    return <EmptyState icon={<Rows3 className="h-8 w-8" />} message={emptyMessage} />;
  }

  // No visible label above it: the selector's own value says which list this is, so a word on top
  // only repeats the control. The accessible name stays on the trigger.
  const selector = (
    <Select value={active.id} onValueChange={setSelectedId}>
      <SelectTrigger id={selectId} aria-label="List" className="h-8 w-56">
        <SelectValue placeholder="Choose a list" />
      </SelectTrigger>
      <SelectContent>
        {entries.map((e) => (
          <SelectItem key={e.id} value={e.id}>
            {e.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );

  return (
    <div className="space-y-4">
      {instanceId ? (
        // The selector rides in the editor's action row rather than above it: one toolbar, not
        // two stacked rows of chrome over the table.
        <ListRowsEditor
          columns={columns}
          instanceId={instanceId}
          displayColumnId={definition?.displayColumnId ?? null}
          readOnly={!canEdit}
          emptyMessage={`No ${active.label.toLowerCase()} yet.`}
          toolbar={selector}
          entityLabel={singularize(active.label)}
        />
      ) : (
        // No holder for the rows yet. The editor would offer an Add button that posts to a URL
        // with `null` in it, so it is not offered at all — but the selector has to stay reachable,
        // or a reader who lands on the empty entry cannot leave it.
        <>
          <div className="flex items-center justify-end">{selector}</div>
          <EmptyState
            icon={<Rows3 className="h-8 w-8" />}
            message={`"${active.label}" has no shared list behind it yet. An administrator creates one under Configuration → List definitions.`}
          />
        </>
      )}
    </div>
  );
}
