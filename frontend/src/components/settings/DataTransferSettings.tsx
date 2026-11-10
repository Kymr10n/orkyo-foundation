/**
 * Data transfer tab: download the organization's data as one archive, and load an archive
 * in place of the organization's data. The import is staged — choose a file, read what the
 * server says about it, then confirm by typing the organization's identifier.
 */
import { useState, type ChangeEvent } from "react";
import { AlertTriangle, CheckCircle2, Download, Upload } from "lucide-react";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { Button } from "@foundation/src/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@foundation/src/components/ui/card";
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import {
  useExportTenantArchive,
  useImportTenantArchive,
  useValidateTenantArchive,
} from "@foundation/src/hooks/useTenantTransfer";
import type { TransferValidationResult } from "@foundation/src/lib/api/transfer-api";
import { SettingsPageHeader } from "./SettingsPageHeader";

const ARCHIVE_EXTENSION = ".orkyoexport";

function totalRows(tables: Record<string, number>): number {
  return Object.values(tables).reduce((sum, count) => sum + count, 0);
}

function ArchiveSummary({ result }: { result: TransferValidationResult }) {
  const source = [result.sourceEdition, result.appVersion && `version ${result.appVersion}`]
    .filter(Boolean)
    .join(", ");

  return (
    <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
      <dt className="text-muted-foreground">Organization</dt>
      <dd>{result.sourceTenantSlug}{source && ` (${source})`}</dd>
      <dt className="text-muted-foreground">Exported</dt>
      <dd>{new Date(result.exportedAt).toLocaleString()}</dd>
      <dt className="text-muted-foreground">Rows</dt>
      <dd>{totalRows(result.tables).toLocaleString()}</dd>
      <dt className="text-muted-foreground">Users found here</dt>
      <dd>{result.matchedUsers}</dd>
    </dl>
  );
}

export function DataTransferSettings() {
  const { membership } = useAuth();
  const tenantSlug = membership?.slug ?? "";

  const [archive, setArchive] = useState<File | null>(null);
  const [progress, setProgress] = useState(0);
  const [confirmOpen, setConfirmOpen] = useState(false);

  const exportMutation = useExportTenantArchive();
  const validateMutation = useValidateTenantArchive(setProgress);
  const importMutation = useImportTenantArchive(setProgress);

  const validation = validateMutation.data;
  const imported = importMutation.data;
  const canImport = archive !== null && validation?.blockers.length === 0;

  const handleFileChange = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0] ?? null;
    setArchive(file);
    setProgress(0);
    validateMutation.reset();
    importMutation.reset();
    if (file) validateMutation.mutate(file);
  };

  const handleImport = () => {
    if (!archive) return;
    setProgress(0);
    importMutation.mutate({ archive, confirmation: tenantSlug });
  };

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="Data transfer"
        description="Move this organization's data to or from another Orkyo installation."
      />

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Download className="h-5 w-5" />
            Export
          </CardTitle>
          <CardDescription>
            Download all data of this organization as one archive. Another Orkyo installation
            can import the archive if it runs the same version.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <Alert>
            <AlertTriangle className="h-4 w-4" />
            <AlertDescription>
              The archive is not encrypted. It contains everything in this organization,
              including floorplans and notes. Keep it as safe as a database backup.
            </AlertDescription>
          </Alert>
          <Button
            onClick={() => exportMutation.mutate()}
            loading={exportMutation.isPending}
            disabled={exportMutation.isPending}
          >
            Download archive
          </Button>
        </CardContent>
      </Card>

      <Card className="border-destructive">
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-destructive">
            <Upload className="h-5 w-5" />
            Import
          </CardTitle>
          <CardDescription>
            Replace all data of this organization with the data from an archive. Users,
            audit history, settings and AI assistant data of this organization stay as they are.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {imported ? (
            <>
              <Alert>
                <CheckCircle2 className="h-4 w-4" />
                <AlertDescription>
                  The import is complete: {imported.rows.toLocaleString()} rows loaded,{" "}
                  {imported.matchedUsers} users matched, {imported.placeholderUsers} kept as
                  inactive placeholders. Reload the page to see the new data.
                </AlertDescription>
              </Alert>
              <Button onClick={() => window.location.reload()}>Reload now</Button>
            </>
          ) : (
            <>
              <div className="space-y-2">
                <Label htmlFor="transfer-archive">Archive file</Label>
                <Input
                  id="transfer-archive"
                  type="file"
                  accept={ARCHIVE_EXTENSION}
                  onChange={handleFileChange}
                  disabled={validateMutation.isPending || importMutation.isPending}
                />
              </div>

              {(validateMutation.isPending || importMutation.isPending) && (
                <p className="text-sm text-muted-foreground" role="status">
                  {importMutation.isPending ? "Importing" : "Checking the archive"}
                  {progress < 100 ? `: ${progress}% uploaded` : ". This can take a minute."}
                </p>
              )}

              {validation && !importMutation.isPending && (
                <>
                  <ArchiveSummary result={validation} />

                  {validation.blockers.map((blocker) => (
                    <Alert key={blocker.code} variant="destructive">
                      <AlertTriangle className="h-4 w-4" />
                      <AlertDescription>{blocker.message}</AlertDescription>
                    </Alert>
                  ))}

                  {validation.unmatchedEmails.length > 0 && (
                    <Alert>
                      <AlertTriangle className="h-4 w-4" />
                      <AlertDescription>
                        These people are not users of this organization. The import keeps
                        their history under an inactive placeholder, and that does not
                        change if they join later. To keep their history with their account,
                        invite them before you import.
                        <ul className="list-disc ml-6 mt-2">
                          {validation.unmatchedEmails.map((email) => (
                            <li key={email}>{email}</li>
                          ))}
                        </ul>
                      </AlertDescription>
                    </Alert>
                  )}

                  <Button variant="destructive" disabled={!canImport} onClick={() => setConfirmOpen(true)}>
                    Import and replace all data
                  </Button>
                </>
              )}
            </>
          )}

          <ConfirmDialog
            open={confirmOpen}
            onOpenChange={setConfirmOpen}
            title="Replace all data?"
            description={
              <>
                This erases all current data of{" "}
                <strong>{membership?.displayName}</strong> and loads the archive in its place.
                It cannot be undone. Other people cannot save changes while the import runs.
                If you need the current data, close this dialog and download an archive first.
              </>
            }
            confirmLabel="Import and replace all data"
            destructive
            confirmPhrase={tenantSlug}
            onConfirm={handleImport}
          />
        </CardContent>
      </Card>
    </div>
  );
}
