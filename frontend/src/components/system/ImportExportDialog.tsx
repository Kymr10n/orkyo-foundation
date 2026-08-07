import { Button } from "@foundation/src/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@foundation/src/components/ui/dialog";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import {
  type ExportContext,
  type ExportFormat,
  type ImportFormat,
  getExportFilename,
} from "@foundation/src/lib/utils/import-export";
import { selectActiveExport, useUiActionsStore } from "@foundation/src/store/ui-actions-store";
import { AlertCircle, Download, Upload } from "lucide-react";
import { Alert, AlertTitle, AlertDescription } from "@foundation/src/components/ui/alert";
import { FeatureUpsell } from "@foundation/src/components/ui/FeatureUpsell";
import { useDataExportAvailable } from "@foundation/src/hooks/useDataExportAvailable";
import { useState, useRef, useEffect } from "react";

interface ImportExportDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  mode: 'import' | 'export';
  context: ExportContext;
  onExport?: (format: ExportFormat) => void;
  onImport?: (file: File, format: ImportFormat) => void;
  siteId?: string;
  /** Where the upsell's CTA points when the tenant's plan lacks the feature. Omit to hide the CTA. */
  upgradeHref?: string;
}

/**
 * Labels, descriptions and formats come from the page's own registration — the
 * static Record<ExportContext, …> tables here could not name a tenant-defined
 * resource type, and drifted from reality every time a page moved.
 */
export function ImportExportDialog({
  open,
  onOpenChange,
  mode,
  context,
  onExport,
  onImport,
  siteId,
  upgradeHref,
}: ImportExportDialogProps) {
  // Gating both modes here covers every registered flow at once: the per-page
  // CSV/JSON exports and imports are built client-side, so this dialog is
  // their only gate. (The organization JSON export is additionally enforced
  // server-side via FeatureKeys.DataExport.)
  const available = useDataExportAvailable();
  const exportRegistry = useUiActionsStore((s) => s.exportRegistry);
  const importRegistry = useUiActionsStore((s) => s.importRegistry);
  const active = selectActiveExport({ exportRegistry, importRegistry });
  const contextLabel = active?.capability.label ?? context;
  const contextDescription = active?.capability.description ?? '';
  const supportedFormats = {
    export: active?.capability.formats ?? [],
    import: active?.importFormats ?? [],
  };
  const defaultExportFormat = supportedFormats.export[0];
  const defaultImportFormat = supportedFormats.import[0];

  const [exportFormat, setExportFormat] = useState<ExportFormat>(defaultExportFormat);
  const [importFormat, setImportFormat] = useState<ImportFormat>(defaultImportFormat);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Reset format when context changes or dialog opens
  useEffect(() => {
    if (open) {
      if (mode === 'export' && defaultExportFormat) setExportFormat(defaultExportFormat);
      if (mode === 'import' && defaultImportFormat) setImportFormat(defaultImportFormat);
      setSelectedFile(null);
    }
  }, [open, context, mode, defaultExportFormat, defaultImportFormat]);

  const handleExport = () => {
    if (onExport && exportFormat) {
      onExport(exportFormat);
      onOpenChange(false);
    }
  };

  const handleImport = async () => {
    if (onImport && selectedFile && importFormat) {
      onImport(selectedFile, importFormat);
      setSelectedFile(null);
      onOpenChange(false);
    }
  };

  const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      setSelectedFile(file);
      // Auto-detect format from extension
      const extension = file.name.split('.').pop()?.toLowerCase();
      if (extension === 'csv' && supportedFormats.import.includes('csv')) {
        setImportFormat('csv');
      } else if (extension === 'json' && supportedFormats.import.includes('json')) {
        setImportFormat('json');
      }
    }
  };

  const canImport = supportedFormats.import.length > 0;

  // The plan lacks the feature: same dialog shell, but the upsell replaces the
  // whole form — there is nothing partial to offer.
  if (!available) {
    return (
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent className="sm:max-w-[500px]">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              {mode === 'export' ? (
                <><Download className="h-5 w-5" /> Export</>
              ) : (
                <><Upload className="h-5 w-5" /> Import</>
              )}
            </DialogTitle>
            <DialogDescription className="sr-only">
              Data export / import is not included in the current plan.
            </DialogDescription>
          </DialogHeader>
          <FeatureUpsell
            title="Data export / import"
            description="Move data in and out of Orkyo — CSV and JSON per page, and a PDF of the schedule. Available on Professional and Enterprise plans."
            upgradeHref={upgradeHref}
          />
        </DialogContent>
      </Dialog>
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            {mode === 'export' ? (
              <>
                <Download className="h-5 w-5" />
                Export {contextLabel}
              </>
            ) : (
              <>
                <Upload className="h-5 w-5" />
                Import {contextLabel}
              </>
            )}
          </DialogTitle>
          <DialogDescription>
            {contextDescription}
          </DialogDescription>
        </DialogHeader>

        {mode === 'export' ? (
          <div className="space-y-4">
            {supportedFormats.export.length > 1 && (
              <div className="space-y-2">
                <Label htmlFor="export-format">Format</Label>
                <Select
                  value={exportFormat}
                  onValueChange={(value) => setExportFormat(value as ExportFormat)}
                >
                  <SelectTrigger id="export-format">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {supportedFormats.export.map((format) => (
                      <SelectItem key={format} value={format}>
                        {format.toUpperCase()}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            )}

            <div className="space-y-2">
              <Label>File name</Label>
              <Input
                value={getExportFilename(context, exportFormat, siteId)}
                readOnly
                className="bg-muted"
              />
            </div>

            {context === 'utilization' && (
              <div className="flex items-start gap-2 p-3 bg-muted rounded-lg">
                <AlertCircle className="h-4 w-4 text-muted-foreground mt-0.5 flex-shrink-0" />
                <p className="text-sm text-muted-foreground">
                  The export will include all scheduled requests visible in the current Gantt chart view.
                </p>
              </div>
            )}
          </div>
        ) : (
          <div className="space-y-4">
            {!canImport ? (
              <Alert variant="destructive">
                <AlertCircle className="h-4 w-4" />
                <AlertTitle>Import not available</AlertTitle>
                <AlertDescription>
                  Import is not supported for {contextLabel.toLowerCase()}.
                </AlertDescription>
              </Alert>
            ) : (
              <>
                <div className="space-y-2">
                  <Label htmlFor="file-upload">Select file</Label>
                  <div className="flex gap-2">
                    <Input
                      id="file-display"
                      value={selectedFile?.name || ''}
                      placeholder="No file selected"
                      readOnly
                      className="bg-muted cursor-pointer"
                      onClick={() => fileInputRef.current?.click()}
                    />
                    <Button
                      type="button"
                      variant="outline"
                      onClick={() => fileInputRef.current?.click()}
                    >
                      Browse
                    </Button>
                  </div>
                  <input
                    ref={fileInputRef}
                    type="file"
                    accept={supportedFormats.import.map(f => `.${f}`).join(',')}
                    onChange={handleFileSelect}
                    className="hidden"
                  />
                </div>

                {supportedFormats.import.length > 1 && (
                  <div className="space-y-2">
                    <Label htmlFor="import-format">Format</Label>
                    <Select
                      value={importFormat}
                      onValueChange={(value) => setImportFormat(value as ImportFormat)}
                    >
                      <SelectTrigger id="import-format">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {supportedFormats.import.map((format) => (
                          <SelectItem key={format} value={format}>
                            {format.toUpperCase()}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                )}

                <div className="flex items-start gap-2 p-3 bg-muted rounded-lg">
                  <AlertCircle className="h-4 w-4 text-muted-foreground mt-0.5 flex-shrink-0" />
                  <p className="text-sm text-muted-foreground">
                    The import will validate data before applying changes.
                    Existing items with matching IDs will be updated.
                  </p>
                </div>
              </>
            )}
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          {mode === 'export' ? (
            <Button onClick={handleExport}>
              <Download className="h-4 w-4 mr-2" />
              Export
            </Button>
          ) : (
            <Button
              onClick={handleImport}
              disabled={!canImport || !selectedFile}
            >
              <Upload className="h-4 w-4 mr-2" />
              Import
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
