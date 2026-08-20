import { useMemo } from 'react';
import { useQueries, useQuery } from '@tanstack/react-query';
import { getResourceCustomFields } from '@foundation/src/lib/api/resource-custom-fields-api';
import { getListDefinition, getListInstance, getListRows } from '@foundation/src/lib/api/lists-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';

/**
 * Resolves the display labels behind a resource type's `list_lookup` custom fields.
 *
 * A lookup value is an array of row ids, so a column or a label that wants to read as words has to
 * go through the bound shared instance. This is the one place that does it, for every caller that
 * shows a lookup outside a form: the resource list's columns and the utilization grid's row labels.
 *
 * Returns a map of resource id → { fieldKey: "label" }. A field with several rows picked joins them
 * with a comma, which is what a single cell can honestly show.
 */
export function useLookupFieldLabels(
  resourceTypeId: string | undefined,
  resources: readonly ResourceInfo[],
  /** Restricts the work to the keys a caller actually renders. Omit for every lookup field. */
  fieldKeys?: readonly string[],
): Record<string, Record<string, string>> {
  const { data: fields = [] } = useQuery({
    queryKey: qk.resourceTypes.customFields(resourceTypeId ?? 'none'),
    queryFn: () => getResourceCustomFields(resourceTypeId!),
    enabled: !!resourceTypeId,
  });

  const lookups = useMemo(
    () =>
      fields.filter(
        (f) =>
          f.dataType === 'list_lookup' &&
          f.isActive &&
          !!f.listInstanceId &&
          (!fieldKeys || fieldKeys.includes(f.key)),
      ),
    [fields, fieldKeys],
  );

  // Rows and the owning definition per bound instance. The definition supplies the display column,
  // which is what decides which cell of a row names it.
  //
  // `combine` is not optional here: without it useQueries hands back a fresh array every render,
  // which would make the memo below recompute every time and hand callers a new object — and the
  // callers memoize grid rows on it.
  const rows = useQueries({
    queries: lookups.map((f) => ({
      queryKey: qk.lists.instanceRows(f.listInstanceId!),
      queryFn: () => getListRows(f.listInstanceId!),
    })),
    combine: (results) => results.map((r) => r.data),
  });
  // A lookup field names its instance and never its definition (the binding CHECK in migration
  // 1780), so the definition — and with it the display column — is one hop further.
  const instances = useQueries({
    queries: lookups.map((f) => ({
      queryKey: qk.lists.instance(f.listInstanceId!),
      queryFn: () => getListInstance(f.listInstanceId!),
    })),
    combine: (results) => results.map((r) => r.data),
  });
  const definitions = useQueries({
    queries: lookups.map((_, i) => {
      const definitionId = instances[i]?.listDefinitionId;
      return {
        queryKey: qk.lists.definition(definitionId ?? 'none'),
        queryFn: () => getListDefinition(definitionId!),
        enabled: !!definitionId,
      };
    }),
    combine: (results) => results.map((r) => r.data),
  });

  return useMemo(() => {
    // row id → label, per field key.
    const labelByRow = new Map<string, Map<string, string>>();
    lookups.forEach((field, i) => {
      const instanceRows = rows[i] ?? [];
      const definition = definitions[i];
      const columns = (definition?.columns ?? []).filter((c) => c.isActive);
      const displayKey =
        columns.find((c) => c.id === definition?.displayColumnId)?.key ?? columns[0]?.key;
      const map = new Map<string, string>();
      for (const row of instanceRows) {
        const value = displayKey ? row.values?.[displayKey] : undefined;
        // A row with nothing in its display cell would render as blank; its id helps nobody, so
        // it is simply absent and the caller falls back the way an unset value does.
        if (value !== undefined && value !== null && value !== '') map.set(row.id, String(value));
      }
      labelByRow.set(field.key, map);
    });

    const out: Record<string, Record<string, string>> = {};
    for (const resource of resources) {
      const values = resource.customFields;
      if (!values) continue;
      const perField: Record<string, string> = {};
      for (const field of lookups) {
        const picked = values[field.key];
        if (!Array.isArray(picked)) continue;
        const labels = picked
          .map((id) => labelByRow.get(field.key)?.get(id))
          .filter((l): l is string => !!l);
        if (labels.length > 0) perField[field.key] = labels.join(', ');
      }
      if (Object.keys(perField).length > 0) out[resource.id] = perField;
    }
    return out;
  }, [lookups, rows, definitions, resources]);
}
