import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  type ExportContext,
  type ExportFormat,
  type ImportFormat,
  getExportFilename,
  getSupportedFormats,
  isImportSupported,
} from "@/lib/utils/import-export";
import { AlertCircle, Download, Upload } from "lucide-react";
import { useState, useRef, useEffect } from "react";

interface ImportExportDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  mode: 'import' | 'export';
  context: ExportContext;
  onExport?: (format: ExportFormat) => void;
  onImport?: (file: File, format: ImportFormat) => void;
  siteId?: string;
}

const contextLabels: Record<ExportContext, string> = {
  utilization: 'Utilization (Gantt Chart)',
  spaces: 'Spaces',
  requests: 'Requests',
  conflicts: 'Conflicts',
  criteria: 'Criteria',
  sites: 'Sites',
  templates: 'Request Templates',
  users: 'Users',
};

const contextDescriptions: Record<ExportContext, string> = {
  utilization: 'Export a PDF visualization of the Gantt chart showing all scheduled requests',
  spaces: 'Export or import the list of spaces with their properties and capabilities',
  requests: 'Export or import all requests with their requirements and constraints',
  conflicts: 'Export the current list of conflicts (import not available)',
  criteria: 'Export or import criteria definitions and their data types',
  sites: 'Export or import site configurations and properties',
  templates: 'Export or import request templates for faster request creation',
  users: 'Export or import user accounts and role assignments',
};

export function ImportExportDialog({
  open,
  onOpenChange,
  mode,
  context,
  onExport,
  onImport,
  siteId,
}: ImportExportDialogProps) {
  const supportedFormats = getSupportedFormats(context);
  const defaultExportFormat = supportedFormats.export[0];
  const defaultImportFormat = supportedFormats.import[0];

  const [exportFormat, setExportFormat] = useState<ExportFormat>(defaultExportFormat);
  const [importFormat, setImportFormat] = useState<ImportFormat>(defaultImportFormat);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Reset format when context changes or dialog opens
  useEffect(() => {
    if (open) {
      const formats = getSupportedFormats(context);
      if (mode === 'export' && formats.export.length > 0) {
        setExportFormat(formats.export[0]);
      }
      if (mode === 'import' && formats.import.length > 0) {
        setImportFormat(formats.import[0]);
      }
      setSelectedFile(null);
    }
  }, [open, context, mode]);

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

  const canImport = isImportSupported(context);

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            {mode === 'export' ? (
              <>
                <Download className="h-5 w-5" />
                Export {contextLabels[context]}
              </>
            ) : (
              <>
                <Upload className="h-5 w-5" />
                Import {contextLabels[context]}
              </>
            )}
          </DialogTitle>
          <DialogDescription>
            {contextDescriptions[context]}
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
              <div className="flex items-start gap-2 p-4 bg-destructive/10 border border-destructive/50 rounded-lg">
                <AlertCircle className="h-5 w-5 text-destructive mt-0.5 flex-shrink-0" />
                <div className="flex-1">
                  <p className="text-sm font-medium text-destructive mb-1">
                    Import not available
                  </p>
                  <p className="text-sm text-destructive/80">
                    Import is not supported for {contextLabels[context].toLowerCase()}.
                  </p>
                </div>
              </div>
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
