import { useCallback } from 'react';
import { qk } from '@foundation/src/lib/api/query-keys';
import { createResource, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import { exportResources, importResources, type ResourceExportRow } from '@foundation/src/lib/utils/export-handlers';
import { resourceContext } from '@foundation/src/lib/utils/import-export';
import { useExportHandler, useImportHandler } from './useImportExport';

interface ResourceTransferOptions {
  /**
   * Extra columns for this type, e.g. a person's profile fields. Returned keys
   * become export columns verbatim; they are ignored on import unless
   * `afterCreate` puts them somewhere.
   */
  extraColumns?: (resource: ResourceInfo) => Record<string, unknown>;
  /**
   * Runs once per imported row after the resource exists — for the side tables
   * a type may own (profiles, capabilities). Failures abort the import.
   */
  afterCreate?: (created: ResourceInfo, sourceRow: Record<string, unknown>) => Promise<void>;
}

/**
 * Registers import/export for one resource type. Every type — the built-in
 * People and Tools as much as one a tenant invented this morning — gets the
 * same offer from the same code path; the type's own fields ride along in
 * `metadata`, so nothing here needs to know what they are.
 *
 * Call it from the page that owns the type's list, once per mounted type.
 */
export function useResourceTransfer(
  resourceType: ResourceTypeInfo,
  resources: ResourceInfo[],
  options: ResourceTransferOptions = {},
) {
  const { extraColumns, afterCreate } = options;
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
      const rows = await importResources(file, format, resourceType.key);
      if (!rows.length) throw new Error(`No valid ${plural.toLowerCase()} found in file`);
      for (const { request, source } of rows) {
        const created = await createResource(request);
        await afterCreate?.(created, source);
      }
      return rows.length;
    },
    {
      successMessage: (count) => `Imported ${count} ${plural.toLowerCase()}`,
      errorMessage: `Failed to import ${plural.toLowerCase()}`,
      formats: ['csv', 'json'],
      invalidates: [qk.resources.byType(resourceType.key)],
    },
  );
}
