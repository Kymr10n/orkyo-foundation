import { useMutation } from "@tanstack/react-query";
import {
  exportTenantArchive,
  importTenantArchive,
  validateTenantArchive,
  type TransferProgress,
} from "@foundation/src/lib/api/transfer-api";
import { downloadFile } from "@foundation/src/lib/utils/import-export";

// The data transfer tab's calls. None of them runs inside a dialog that stays open, so each
// reports a failure through its `meta` toast, whose description is the server's own reason
// (a schema version that does not match, an archive that is too large). None invalidates:
// after an import every cached query is stale, and the tab reloads the page instead.

/** Download the organization's archive under the file name the server gives it. */
export const useExportTenantArchive = () =>
  useMutation({
    mutationFn: async () => {
      const { blob, fileName } = await exportTenantArchive();
      downloadFile(blob, fileName, "application/zip");
    },
    meta: { errorMessage: "Could not export the organization data" },
  });

/** Ask the server whether an archive can be imported here. Changes nothing. */
export const useValidateTenantArchive = (onProgress: (progress: TransferProgress) => void) =>
  useMutation({
    mutationFn: (archive: File) => validateTenantArchive(archive, onProgress),
    meta: { errorMessage: "Could not read the archive" },
  });

/** Replace the organization's data with an archive. */
export const useImportTenantArchive = (onProgress: (progress: TransferProgress) => void) =>
  useMutation({
    mutationFn: ({ archive, confirmation }: { archive: File; confirmation: string }) =>
      importTenantArchive(archive, confirmation, onProgress),
    meta: { errorMessage: "Could not import the archive" },
  });
