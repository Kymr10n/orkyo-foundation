import { useCallback, useMemo } from 'react';
import { qk } from '@foundation/src/lib/api/query-keys';
import { createResource, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import { exportResources, importResources, type ResourceExportRow } from '@foundation/src/lib/utils/export-handlers';
import { resourceContext } from '@foundation/src/lib/utils/import-export';
import { useExportHandler, useImportHandler } from './useImportExport';
import { useResourceCustomFields } from './useResourceCustomFields';

interface ResourceTransferOptions {
  /**
   * Extra columns for this type, e.g. a person's profile fields. Returned keys
   * become export columns verbatim; they are ignored on import unless
   * `afterCreate` puts them somewhere.
   */
  extraColumns?: (resource: ResourceInfo) => Record<string, unknown>;
  /**
   * Runs once per imported row after the resource exists — for the side tables
   * a type may own (profiles, capabilities). A failure fails that row only; the
   * rest of the file still imports and the count reports what landed.
   */
  afterCreate?: (created: ResourceInfo, sourceRow: Record<string, unknown>) => Promise<void>;
}

/**
 * Registers import/export for one resource type. Every type — the built-in
 * People and Tools as much as one a tenant invented this morning — gets the
 * same offer from the same code path; the type's own custom fields ride along in
 * `customFields`, so nothing here needs to know what they are.
 *
 * Call it from the page that owns the type's list, once per mounted type.
 */
export function useResourceTransfer(
  resourceType: ResourceTypeInfo,
  resources: ResourceInfo[],
  options: ResourceTransferOptions = {},
) {
  const { extraColumns, afterCreate } = options;
  // The type's field definitions decide how a CSV cell is read back — a bare "1200" is a
  // number for one field and text for another, so the declared type has to be to hand.
  const { data: customFields = [] } = useResourceCustomFields(resourceType.id);
  const customFieldTypes = useMemo(
    () => Object.fromEntries(customFields.map((f) => [f.key, f.dataType])),
    [customFields],
  );
  const context = resourceContext(resourceType.key);
  const plural = resourceType.displayNamePlural;

  const buildRows = useCallback(
    (): ResourceExportRow[] =>
      resources.map((resource) => ({ ...resource, ...(extraColumns?.(resource) ?? {}) })),
    [resources, extraColumns],
  );

  useExportHandler(
    context,
    async (format) => {
      await exportResources(buildRows(), format, resourceType.key);
    },
    {
      label: plural,
      description: `Export or import ${plural.toLowerCase()} with their properties and custom fields.`,
      formats: ['csv', 'json'],
    },
  );

  useImportHandler(
    context,
    async (file, format) => {
      const rows = await importResources(file, format, resourceType.key, customFieldTypes);
      if (!rows.length) throw new Error(`No valid ${plural.toLowerCase()} found in file`);

      // Rows are created one by one and the earlier ones are already committed, so a row the
      // server rejects must not hide the rest: keep going, then report what actually landed.
      // Custom-field values are validated server-side, which makes a single bad cell likely.
      const failures: string[] = [];
      let imported = 0;
      const reason = (err: unknown) => (err instanceof Error ? err.message : 'rejected');

      for (const { request, source } of rows) {
        let created: ResourceInfo;
        try {
          created = await createResource(request);
        } catch (err) {
          failures.push(`${request.name}: ${reason(err)}`);
          continue;
        }

        // Counted from here on: the resource exists. A failing follow-up leaves side-table work
        // undone, but re-importing the row to fix it would create a second copy — so say what
        // actually happened rather than calling the row rejected.
        imported++;
        try {
          await afterCreate?.(created, source);
        } catch (err) {
          failures.push(`${request.name}: created, but ${reason(err)}`);
        }
      }

      if (failures.length) {
        throw new Error(
          `Imported ${imported} of ${rows.length}. ${failures.length} rejected — ${failures[0]}`,
        );
      }
      return imported;
    },
    {
      successMessage: (count) => `Imported ${count} ${plural.toLowerCase()}`,
      errorMessage: `Failed to import ${plural.toLowerCase()}`,
      formats: ['csv', 'json'],
      invalidates: [qk.resources.byType(resourceType.key)],
    },
  );
}
