import { useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { CheckCircle2, FileSpreadsheet, Loader2 } from 'lucide-react';
import { ScaffoldDialog } from '@foundation/src/components/ui/ScaffoldDialog';
import { ScrollableDialogBody, DialogFooter } from '@foundation/src/components/ui/dialog';
import { Alert, AlertDescription } from '@foundation/src/components/ui/alert';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { Button } from '@foundation/src/components/ui/button';
import { Label } from '@foundation/src/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import { FeatureUpsell } from '@foundation/src/components/ui/FeatureUpsell';
import { useDataExportAvailable } from '@foundation/src/hooks/useDataExportAvailable';
import { useSites } from '@foundation/src/hooks/useSites';
import { useAppStore } from '@foundation/src/store/app-store';
import { createResource, getResources } from '@foundation/src/lib/api/resources-api';
import { createRequest } from '@foundation/src/lib/api/request-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import { invalidateRequestData } from '@foundation/src/lib/core/invalidate-request-data';
import { readWorkbook } from '@foundation/src/lib/utils/spreadsheet-file';
import {
  jobToCreateRequest,
  parseTemplateWorkbook,
  workstationToCreateSpace,
  type ParsedWorkbook,
} from '@foundation/src/lib/utils/spreadsheet-import';
import { errorMessage } from '@foundation/src/hooks/mutation-utils';

interface SpreadsheetImportWizardProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Where the upsell's CTA points when the tenant's plan lacks the feature. */
  upgradeHref?: string;
}

type Step =
  | { kind: 'pick' }
  | { kind: 'preview'; parsed: ParsedWorkbook; existingCodes: Map<string, string> }
  | { kind: 'committing'; done: number; total: number }
  | {
      kind: 'result';
      createdWorkstations: number;
      reusedWorkstations: number;
      createdJobs: number;
      failure?: string;
    };

/**
 * Loads the published capacity-planning template (orkyo.com/guides/
 * capacity-planning-excel-template/) into a site: the Workstations sheet becomes
 * spaces, the Jobs sheet becomes requests assigned to them.
 *
 * Its own dialog rather than the shared ImportExportDialog because that one is
 * single-context and fire-and-forget — it has nowhere to put a two-sheet
 * workbook, a preview, or the code→resource resolution between the two sheets.
 */
export function SpreadsheetImportWizard({
  open,
  onOpenChange,
  upgradeHref,
}: SpreadsheetImportWizardProps) {
  // The template's Workstations sheet becomes resources of the tenant's placeable type —
  // `space` where it still exists (the template's historical meaning), else whatever placeable
  // type the tenant kept. Null when no placeable type is active: nothing is built in, so the
  // import blocks with a pointer at the catalog instead of inventing a key.
  const { data: resourceTypes = [] } = useResourceTypes(true);
  const placeableTypes = resourceTypes.filter((t) => t.hasGeometry);
  const workstationTypeKey =
    placeableTypes.find((t) => t.key === 'space')?.key ?? placeableTypes[0]?.key ?? null;

  const available = useDataExportAvailable();
  const queryClient = useQueryClient();
  const { data: sites = [] } = useSites();
  const storeSiteId = useAppStore((s) => s.selectedSiteId);
  const [siteId, setSiteId] = useState<string | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [step, setStep] = useState<Step>({ kind: 'pick' });
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const effectiveSiteId = siteId ?? storeSiteId ?? sites[0]?.id ?? null;

  const reset = () => {
    setStep({ kind: 'pick' });
    setFile(null);
    setLoadError(null);
    setBusy(false);
  };

  const handleOpenChange = (next: boolean) => {
    if (!next) reset();
    onOpenChange(next);
  };

  const analyze = async () => {
    if (!file || !effectiveSiteId || !workstationTypeKey) return;
    setBusy(true);
    setLoadError(null);
    try {
      const sheets = await readWorkbook(file);
      const parsed = parseTemplateWorkbook(sheets);
      const existing = (await getResources({ hasGeometry: true, isActive: true, siteId: effectiveSiteId })).data;
      const existingCodes = new Map(
        existing.filter((s) => s.code).map((s) => [s.code as string, s.id]),
      );
      setStep({ kind: 'preview', parsed, existingCodes });
    } catch (err) {
      setLoadError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  const commit = async (parsed: ParsedWorkbook, existingCodes: Map<string, string>) => {
    if (!effectiveSiteId || !workstationTypeKey) return;
    const toCreate = parsed.workstations.filter((w) => !existingCodes.has(w.code));
    const total = toCreate.length + parsed.jobs.length;
    setStep({ kind: 'committing', done: 0, total });

    const codeToResourceId = new Map(existingCodes);
    let createdWorkstations = 0;
    let createdJobs = 0;
    let done = 0;

    try {
      // Workstations first, so a failure partway leaves a usable state — places
      // without jobs, rather than jobs pointing at places that don't exist.
      for (const workstation of toCreate) {
        const space = await createResource(
          workstationToCreateSpace(workstation, effectiveSiteId, workstationTypeKey));
        codeToResourceId.set(workstation.code, space.id);
        createdWorkstations++;
        setStep({ kind: 'committing', done: ++done, total });
      }
      for (const job of parsed.jobs) {
        await createRequest(jobToCreateRequest(job, codeToResourceId, effectiveSiteId));
        createdJobs++;
        setStep({ kind: 'committing', done: ++done, total });
      }
      setStep({
        kind: 'result',
        createdWorkstations,
        reusedWorkstations: parsed.workstations.length - toCreate.length,
        createdJobs,
      });
    } catch (err) {
      setStep({
        kind: 'result',
        createdWorkstations,
        reusedWorkstations: parsed.workstations.length - toCreate.length,
        createdJobs,
        failure: errorMessage(err),
      });
    } finally {
      queryClient.invalidateQueries({ queryKey: qk.resources.all() });
      invalidateRequestData(queryClient);
    }
  };

  const siteName = useMemo(
    () => sites.find((s) => s.id === effectiveSiteId)?.name ?? '',
    [sites, effectiveSiteId],
  );

  return (
    <ScaffoldDialog
      open={open}
      onOpenChange={handleOpenChange}
      size="md"
      contentClassName="h-auto max-h-[85dvh]"
      title={
        <span className="flex items-center gap-2">
          <FileSpreadsheet className="h-5 w-5" />
          Import from spreadsheet
        </span>
      }
      description="Load workstations and jobs from the Orkyo capacity-planning template (.xlsx)."
    >
      <ScrollableDialogBody className="space-y-4 px-6 pb-6">
        {!available ? (
          <FeatureUpsell
            title="Data import"
            description="Importing data from files is available on paid plans."
            upgradeHref={upgradeHref}
          />
        ) : step.kind === 'pick' ? (
          <>
            {!workstationTypeKey && (
              <ErrorAlert message="No placeable resource type is active. Activate one under Configuration → Type catalog before importing workstations." />
            )}
            <div className="space-y-2">
              <Label htmlFor="spreadsheet-file">Template file</Label>
              <input
                id="spreadsheet-file"
                type="file"
                accept=".xlsx"
                className="block w-full text-sm file:mr-3 file:rounded-md file:border-0 file:bg-secondary file:px-3 file:py-1.5 file:text-sm"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="spreadsheet-site">Target site</Label>
              <Select value={effectiveSiteId ?? undefined} onValueChange={setSiteId}>
                <SelectTrigger id="spreadsheet-site">
                  <SelectValue placeholder="Choose a site" />
                </SelectTrigger>
                <SelectContent>
                  {sites.map((site) => (
                    <SelectItem key={site.id} value={site.id}>
                      {site.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <ErrorAlert message={loadError} />
          </>
        ) : step.kind === 'preview' ? (
          <div className="space-y-3 text-sm">
            <p>
              <strong>{step.parsed.workstations.length} workstations</strong> and{' '}
              <strong>{step.parsed.jobs.length} jobs</strong> found
              {siteName ? (
                <>
                  {' '}
                  — importing into <strong>{siteName}</strong>
                </>
              ) : null}
              .
            </p>
            {(() => {
              const reused = step.parsed.workstations.filter((w) =>
                step.existingCodes.has(w.code),
              ).length;
              return reused > 0 ? (
                <p className="text-muted-foreground">
                  {reused} workstation {reused === 1 ? 'code already exists' : 'codes already exist'}{' '}
                  on this site and will be reused, not duplicated.
                </p>
              ) : null;
            })()}
            <p className="text-muted-foreground">
              Daily capacity hours are not imported — a workstation&apos;s capacity in Orkyo is how
              many things it holds at once, not hours per day.
            </p>
            {step.parsed.errors.length > 0 && (
              <Alert>
                <AlertDescription className="space-y-1">
                  <p className="font-medium">
                    {step.parsed.errors.length}{' '}
                    {step.parsed.errors.length === 1 ? 'row' : 'rows'} will be skipped:
                  </p>
                  {step.parsed.errors.map((error, i) => (
                    <p key={i} className="text-muted-foreground">
                      {error.sheet}
                      {error.row > 0 ? ` row ${error.row}` : ''}: {error.message}
                    </p>
                  ))}
                </AlertDescription>
              </Alert>
            )}
          </div>
        ) : step.kind === 'committing' ? (
          <div className="flex items-center gap-3 py-6 text-sm text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
            Importing… {step.done} of {step.total}
          </div>
        ) : step.failure ? (
          <ErrorAlert
            message={`Import stopped: ${step.failure}. Created before stopping: ${step.createdWorkstations} workstations and ${step.createdJobs} jobs — they remain in place.`}
          />
        ) : (
          <Alert>
            <CheckCircle2 className="h-4 w-4" />
            <AlertDescription>
              Created {step.createdWorkstations} workstations
              {step.reusedWorkstations > 0 ? ` (${step.reusedWorkstations} reused)` : ''} and{' '}
              {step.createdJobs} jobs. Conflicts, if any, are already visible on the schedule.
            </AlertDescription>
          </Alert>
        )}
      </ScrollableDialogBody>

      <DialogFooter className="px-6 pb-6">
        {step.kind === 'pick' && available && (
          <Button onClick={analyze} disabled={!file || !effectiveSiteId || !workstationTypeKey || busy}>
            {busy && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Review
          </Button>
        )}
        {step.kind === 'preview' && (
          <>
            <Button variant="outline" onClick={reset}>
              Back
            </Button>
            <Button
              onClick={() => commit(step.parsed, step.existingCodes)}
              disabled={step.parsed.workstations.length === 0 && step.parsed.jobs.length === 0}
            >
              Import
            </Button>
          </>
        )}
        {step.kind === 'result' && <Button onClick={() => handleOpenChange(false)}>Done</Button>}
      </DialogFooter>
    </ScaffoldDialog>
  );
}
