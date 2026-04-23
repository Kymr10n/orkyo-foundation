import { Alert, AlertDescription, AlertTitle } from "@foundation/src/components/ui/alert";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@foundation/src/components/ui/card";
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
import { Textarea } from "@foundation/src/components/ui/textarea";
import {
  applyPreset,
  downloadPreset,
  exportPreset,
  getPresetApplications,
  parsePresetFile,
  validatePreset,
  type Preset,
  type PresetApplication,
  type PresetApplicationResult,
  type PresetValidationResult,
} from "@foundation/src/lib/api/preset-api";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertCircle,
  CheckCircle2,
  Download,
  FileJson,
  History,
  Loader2,
  Upload,
} from "lucide-react";
import { useRef, useState } from "react";

export function PresetSettings() {
  const queryClient = useQueryClient();
  const fileInputRef = useRef<HTMLInputElement>(null);
  
  // State for dialogs
  const [_importDialogOpen, _setImportDialogOpen] = useState(false);
  const [exportDialogOpen, setExportDialogOpen] = useState(false);
  const [previewDialogOpen, setPreviewDialogOpen] = useState(false);
  
  // State for import
  const [importedPreset, setImportedPreset] = useState<Preset | null>(null);
  const [validationResult, setValidationResult] = useState<PresetValidationResult | null>(null);
  const [applicationResult, setApplicationResult] = useState<PresetApplicationResult | null>(null);
  
  // State for export
  const [exportPresetId, setExportPresetId] = useState("");
  const [exportName, setExportName] = useState("");
  const [exportDescription, setExportDescription] = useState("");

  // Load preset application history
  const { data: applications = [], isLoading: loadingHistory } = useQuery({
    queryKey: ["preset-applications"],
    queryFn: getPresetApplications,
  });

  // Validate mutation
  const validateMutation = useMutation({
    mutationFn: validatePreset,
    onSuccess: (result) => {
      setValidationResult(result);
    },
  });

  // Apply mutation
  const applyMutation = useMutation({
    mutationFn: applyPreset,
    onSuccess: (result) => {
      setApplicationResult(result);
      if (result.success) {
        queryClient.invalidateQueries({ queryKey: ["preset-applications"] });
        queryClient.invalidateQueries({ queryKey: ["criteria"] });
        queryClient.invalidateQueries({ queryKey: ["space-groups"] });
        queryClient.invalidateQueries({ queryKey: ["templates-request"] });
        queryClient.invalidateQueries({ queryKey: ["templates-space"] });
        queryClient.invalidateQueries({ queryKey: ["templates-group"] });
      }
    },
  });

  // Export mutation
  const exportMutation = useMutation({
    mutationFn: () => exportPreset(exportPresetId, exportName, exportDescription || undefined),
    onSuccess: (preset) => {
      downloadPreset(preset);
      setExportDialogOpen(false);
      resetExportForm();
    },
  });

  const handleFileSelect = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    try {
      const content = await file.text();
      const preset = parsePresetFile(content);
      setImportedPreset(preset);
      setValidationResult(null);
      setApplicationResult(null);
      setPreviewDialogOpen(true);
      
      // Auto-validate
      validateMutation.mutate(preset);
    } catch (error) {
      alert(error instanceof Error ? error.message : "Failed to parse preset file");
    }

    // Reset file input
    if (fileInputRef.current) {
      fileInputRef.current.value = "";
    }
  };

  const handleApply = () => {
    if (!importedPreset) return;
    applyMutation.mutate(importedPreset);
  };

  const resetImportState = () => {
    setImportedPreset(null);
    setValidationResult(null);
    setApplicationResult(null);
    setPreviewDialogOpen(false);
  };

  const resetExportForm = () => {
    setExportPresetId("");
    setExportName("");
    setExportDescription("");
  };

  const formatDate = (dateStr: string) => {
    return new Date(dateStr).toLocaleDateString(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    });
  };

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="Presets"
        description="Import, export, and manage tenant configuration presets."
      />

      {/* Actions */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <FileJson className="h-5 w-5" />
            Presets
          </CardTitle>
          <CardDescription>
            Import or export tenant configuration presets. Presets include criteria,
            space groups, and templates.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex gap-4">
          <input
            type="file"
            ref={fileInputRef}
            accept=".json"
            className="hidden"
            onChange={handleFileSelect}
          />
          <Button onClick={() => fileInputRef.current?.click()}>
            <Upload className="mr-2 h-4 w-4" />
            Import Preset
          </Button>
          <Button variant="outline" onClick={() => setExportDialogOpen(true)}>
            <Download className="mr-2 h-4 w-4" />
            Export Current Configuration
          </Button>
        </CardContent>
      </Card>

      {/* Application History */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <History className="h-5 w-5" />
            Application History
          </CardTitle>
          <CardDescription>
            Presets that have been applied to this tenant.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {loadingHistory ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
            </div>
          ) : applications.length === 0 ? (
            <p className="text-sm text-muted-foreground py-4">
              No presets have been applied to this tenant yet.
            </p>
          ) : (
            <div className="space-y-3">
              {applications.map((app: PresetApplication) => (
                <div
                  key={app.id}
                  className="flex items-center justify-between p-3 border rounded-lg"
                >
                  <div>
                    <div className="font-medium">{app.presetId}</div>
                    <div className="text-sm text-muted-foreground">
                      Applied: {formatDate(app.appliedAt)}
                      {app.updatedAt && ` • Updated: ${formatDate(app.updatedAt)}`}
                    </div>
                  </div>
                  <Badge variant="outline">v{app.presetVersion}</Badge>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      {/* Preview/Apply Dialog */}
      <Dialog open={previewDialogOpen} onOpenChange={(open) => !open && resetImportState()}>
        <DialogContent className="max-w-2xl max-h-[80vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>
              {applicationResult?.success ? "Preset Applied" : "Preview Preset"}
            </DialogTitle>
            <DialogDescription>
              {applicationResult?.success
                ? "The preset has been successfully applied."
                : "Review the preset before applying it to your tenant."}
            </DialogDescription>
          </DialogHeader>

          {importedPreset && (
            <div className="space-y-4">
              {/* Preset Info */}
              <div className="grid gap-2">
                <div className="flex items-center gap-2">
                  <span className="font-semibold">{importedPreset.name}</span>
                  <Badge variant="outline">v{importedPreset.version}</Badge>
                </div>
                {importedPreset.description && (
                  <p className="text-sm text-muted-foreground">
                    {importedPreset.description}
                  </p>
                )}
                <div className="text-xs text-muted-foreground">
                  ID: {importedPreset.presetId}
                  {importedPreset.vendor && ` • Vendor: ${importedPreset.vendor}`}
                  {importedPreset.industry && ` • Industry: ${importedPreset.industry}`}
                </div>
              </div>

              {/* Contents Summary */}
              <div className="grid grid-cols-3 gap-4">
                <Card>
                  <CardContent className="pt-4">
                    <div className="text-2xl font-bold">
                      {importedPreset.contents.criteria.length}
                    </div>
                    <div className="text-sm text-muted-foreground">Criteria</div>
                  </CardContent>
                </Card>
                <Card>
                  <CardContent className="pt-4">
                    <div className="text-2xl font-bold">
                      {importedPreset.contents.spaceGroups.length}
                    </div>
                    <div className="text-sm text-muted-foreground">Space Groups</div>
                  </CardContent>
                </Card>
                <Card>
                  <CardContent className="pt-4">
                    <div className="text-2xl font-bold">
                      {importedPreset.contents.templates.request.length +
                        importedPreset.contents.templates.space.length +
                        importedPreset.contents.templates.group.length}
                    </div>
                    <div className="text-sm text-muted-foreground">Templates</div>
                  </CardContent>
                </Card>
              </div>

              {/* Validation Result */}
              {validateMutation.isPending && (
                <div className="flex items-center gap-2 text-muted-foreground">
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Validating preset...
                </div>
              )}

              {validationResult && !validationResult.isValid && (
                <Alert variant="destructive">
                  <AlertCircle className="h-4 w-4" />
                  <AlertTitle>Validation Failed</AlertTitle>
                  <AlertDescription>
                    <ul className="list-disc list-inside mt-2">
                      {validationResult.errors.map((error, i) => (
                        <li key={i}>{error}</li>
                      ))}
                    </ul>
                  </AlertDescription>
                </Alert>
              )}

              {validationResult?.isValid && !applicationResult && (
                <Alert>
                  <CheckCircle2 className="h-4 w-4" />
                  <AlertTitle>Validation Passed</AlertTitle>
                  <AlertDescription>
                    The preset is valid and ready to be applied.
                  </AlertDescription>
                </Alert>
              )}

              {/* Application Result */}
              {applicationResult?.success && (
                <Alert className="border-green-500 bg-green-50 dark:bg-green-950">
                  <CheckCircle2 className="h-4 w-4 text-green-600" />
                  <AlertTitle className="text-green-700 dark:text-green-300">
                    Successfully Applied
                  </AlertTitle>
                  <AlertDescription className="text-green-600 dark:text-green-400">
                    <div className="grid grid-cols-2 gap-2 mt-2 text-sm">
                      <div>Criteria created: {applicationResult.stats.criteriaCreated}</div>
                      <div>Criteria updated: {applicationResult.stats.criteriaUpdated}</div>
                      <div>Groups created: {applicationResult.stats.spaceGroupsCreated}</div>
                      <div>Groups updated: {applicationResult.stats.spaceGroupsUpdated}</div>
                      <div>Templates created: {applicationResult.stats.templatesCreated}</div>
                      <div>Templates updated: {applicationResult.stats.templatesUpdated}</div>
                    </div>
                  </AlertDescription>
                </Alert>
              )}

              {applicationResult && !applicationResult.success && (
                <Alert variant="destructive">
                  <AlertCircle className="h-4 w-4" />
                  <AlertTitle>Application Failed</AlertTitle>
                  <AlertDescription>{applicationResult.error}</AlertDescription>
                </Alert>
              )}
            </div>
          )}

          <DialogFooter>
            {applicationResult?.success ? (
              <Button onClick={resetImportState}>Done</Button>
            ) : (
              <>
                <Button variant="outline" onClick={resetImportState}>
                  Cancel
                </Button>
                <Button
                  onClick={handleApply}
                  disabled={
                    !validationResult?.isValid ||
                    applyMutation.isPending
                  }
                >
                  {applyMutation.isPending && (
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  )}
                  Apply Preset
                </Button>
              </>
            )}
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Export Dialog */}
      <Dialog open={exportDialogOpen} onOpenChange={setExportDialogOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Export Configuration as Preset</DialogTitle>
            <DialogDescription>
              Export your current tenant configuration (criteria, groups, templates)
              as a reusable preset file.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="presetId">Preset ID</Label>
              <Input
                id="presetId"
                placeholder="e.g., my-company-v1"
                value={exportPresetId}
                onChange={(e) => setExportPresetId(e.target.value)}
              />
              <p className="text-xs text-muted-foreground">
                A unique identifier for this preset (lowercase, hyphens allowed)
              </p>
            </div>

            <div className="space-y-2">
              <Label htmlFor="name">Name</Label>
              <Input
                id="name"
                placeholder="e.g., My Company Configuration"
                value={exportName}
                onChange={(e) => setExportName(e.target.value)}
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="description">Description (optional)</Label>
              <Textarea
                id="description"
                placeholder="Describe what this preset contains..."
                value={exportDescription}
                onChange={(e) => setExportDescription(e.target.value)}
              />
            </div>
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={() => setExportDialogOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={() => exportMutation.mutate()}
              disabled={!exportPresetId || !exportName || exportMutation.isPending}
            >
              {exportMutation.isPending && (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              )}
              <Download className="mr-2 h-4 w-4" />
              Export
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
