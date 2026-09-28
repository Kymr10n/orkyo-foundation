import { useCallback } from "react";
import { createResource, getResources } from "@foundation/src/lib/api/resources-api";
import { createRequest } from "@foundation/src/lib/api/request-api";
import { useInvalidateImportedData } from "@foundation/src/hooks/useImportExport";
import {
  jobToCreateRequest,
  workstationToCreateSpace,
  type ParsedWorkbook,
} from "@foundation/src/lib/utils/spreadsheet-import";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";

export interface SpreadsheetImportResult {
  createdWorkstations: number;
  reusedWorkstations: number;
  createdJobs: number;
  /** Set when a write failed partway; what was written before it stays. */
  failure?: string;
}

/**
 * The capacity-planning template import's server side: which workstation codes the site
 * already has, and the write of the parsed workbook one record at a time. The wizard keeps
 * the steps and the progress display.
 */
export function useSpreadsheetImport() {
  const invalidateImportedData = useInvalidateImportedData();

  /** The site's placeable resources by code, so the preview can reuse them. */
  const loadExistingCodes = useCallback(async (siteId: string) => {
    const existing = (await getResources({ hasGeometry: true, isActive: true, siteId })).items;
    return new Map(existing.filter((s) => s.code).map((s) => [s.code as string, s.id]));
  }, []);

  const commit = useCallback(
    async (
      parsed: ParsedWorkbook,
      existingCodes: Map<string, string>,
      siteId: string,
      workstationTypeKey: string,
      onProgress: (done: number, total: number) => void,
    ): Promise<SpreadsheetImportResult> => {
      const toCreate = parsed.workstations.filter((w) => !existingCodes.has(w.code));
      const total = toCreate.length + parsed.jobs.length;
      const reusedWorkstations = parsed.workstations.length - toCreate.length;
      onProgress(0, total);

      const codeToResourceId = new Map(existingCodes);
      let createdWorkstations = 0;
      let createdJobs = 0;
      let done = 0;

      try {
        // Workstations first, so a failure partway leaves a usable state — places
        // without jobs, rather than jobs pointing at places that don't exist.
        for (const workstation of toCreate) {
          const space = await createResource(
            workstationToCreateSpace(workstation, siteId, workstationTypeKey));
          codeToResourceId.set(workstation.code, space.id);
          createdWorkstations++;
          onProgress(++done, total);
        }
        for (const job of parsed.jobs) {
          await createRequest(jobToCreateRequest(job, codeToResourceId, siteId));
          createdJobs++;
          onProgress(++done, total);
        }
        return { createdWorkstations, reusedWorkstations, createdJobs };
      } catch (err) {
        return { createdWorkstations, reusedWorkstations, createdJobs, failure: errorMessage(err) };
      } finally {
        void invalidateImportedData();
      }
    },
    [invalidateImportedData],
  );

  return { loadExistingCodes, commit };
}
