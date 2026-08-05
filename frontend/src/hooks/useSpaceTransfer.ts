import { qk } from '@foundation/src/lib/api/query-keys';
import { useCreateSpace, useSpaces } from '@foundation/src/hooks/useSpaces';
import type { CreateSpaceRequest } from '@foundation/src/types/space';
import { exportSpaces, importSpaces } from '@foundation/src/lib/utils/export-handlers';
import { useExportHandler, useImportHandler } from './useImportExport';

/**
 * Registers import/export for spaces.
 *
 * Lives in a hook so the *page* can mount it: the handlers used to sit inside
 * the floorplan panel, which meant the Spaces list tab silently offered no
 * export at all. Spaces keep their own transfer rather than the generic
 * resource one because their geometry and capacity columns have no equivalent
 * on a plain resource — see useResourceTransfer for every other type.
 */
export function useSpaceTransfer(siteId: string | null) {
  const { data: spaces = [] } = useSpaces(siteId);
  // The mutation is only reachable from the import handler, which the TopBar
  // cannot invoke without a selected site.
  const createSpace = useCreateSpace(siteId ?? '');

  useExportHandler(
    'spaces',
    async (format) => {
      await exportSpaces(spaces, format, siteId ?? undefined);
    },
    {
      label: 'Spaces',
      description: 'Export or import spaces with their properties and geometry.',
      formats: ['csv'],
    },
  );

  useImportHandler(
    'spaces',
    async (file, format) => {
      const imported = await importSpaces(file, format);
      if (!imported.length) throw new Error('No valid spaces found in file');
      for (const space of imported) {
        await createSpace.mutateAsync(space as CreateSpaceRequest);
      }
      return imported.length;
    },
    {
      successMessage: (count) => `Imported ${count} spaces`,
      errorMessage: 'Failed to import spaces',
      formats: ['csv'],
      invalidates: [qk.spaces.list(siteId)],
    },
  );
}
