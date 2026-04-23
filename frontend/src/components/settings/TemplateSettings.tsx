 
import { Badge } from "@foundation/src/components/ui/badge";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { Button } from "@foundation/src/components/ui/button";
import { Card } from "@foundation/src/components/ui/card";
import {
    deleteTemplate,
    getTemplates,
    createTemplate,
} from "@foundation/src/lib/api/template-api";
import type { Template, CreateTemplateRequest } from "@foundation/src/types/templates";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertCircle, Clock, Edit, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { CreateTemplateDialog } from "./CreateTemplateDialog";
import { EditTemplateDialog } from "./EditTemplateDialog";
import { useExportHandler, useImportHandler } from '@foundation/src/hooks/useImportExport';
import { exportTemplates, importTemplates } from '@foundation/src/lib/utils/export-handlers';
import { logger } from '@foundation/src/lib/core/logger';

interface TemplateSettingsProps {
  entityType?: 'request' | 'space' | 'group';
}

export function TemplateSettings({ entityType = 'request' }: TemplateSettingsProps) {
  const queryClient = useQueryClient();
  const [createDialogOpen, setCreateDialogOpen] = useState(false);
  const [editingTemplate, setEditingTemplate] =
    useState<Template | null>(null);

  // Load templates with React Query
  const {
    data: templates = [],
    isLoading,
    error,
    refetch,
  } = useQuery({
    queryKey: [`templates-${entityType}`],
    queryFn: () => getTemplates(entityType),
  });

  // Delete mutation
  const deleteMutation = useMutation({
    mutationFn: deleteTemplate,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [`templates-${entityType}`] });
    },
  });

  // Handle export/import
  useExportHandler('templates', async (format) => {
    await exportTemplates(templates, format);
    logger.info(`Exported ${templates.length} templates as ${format.toUpperCase()}`);
  });

  useImportHandler('templates', async (file, format) => {
    try {
      const importedTemplates = await importTemplates(file, format);
      if (!importedTemplates.length) {
        throw new Error('No valid templates found in file');
      }
      // Create templates via API
      for (const template of importedTemplates) {
        await createTemplate(template as CreateTemplateRequest);
      }
      // Reload templates
      queryClient.invalidateQueries({ queryKey: [`templates-${entityType}`] });
      alert(`Successfully imported ${importedTemplates.length} templates`);
    } catch (error) {
      logger.error('Import failed:', error);
      alert(error instanceof Error ? error.message : 'Failed to import templates');
    }
  });

  const handleCreateSuccess = () => {
    setCreateDialogOpen(false);
  };

  const handleUpdateSuccess = () => {
    setEditingTemplate(null);
  };

  const handleDelete = async (template: Template) => {
    if (
      !confirm(
        `Delete template "${template.name}"? This action cannot be undone.`
      )
    ) {
      return;
    }

    try {
      await deleteMutation.mutateAsync(template.id);
    } catch (err) {
      alert(
        err instanceof Error ? err.message : "Failed to delete template"
      );
    }
  };

  const getDurationLabel = (template: Template) => {
    if (!template.durationUnit) return 'No duration set';
    return `${template.durationValue} ${template.durationUnit}`;
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <p className="text-muted-foreground">Loading request templates...</p>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <SettingsPageHeader
        title="Templates"
        description="Create reusable templates for common utilization request patterns. Templates define duration and timing constraints that can be applied to new requests."
      >
        <Button onClick={() => setCreateDialogOpen(true)}>
          <Plus className="h-4 w-4 mr-2" />
          Add Template
        </Button>
      </SettingsPageHeader>

      {/* Error State */}
      {error && (
        <div className="flex items-center gap-2 p-4 border border-destructive/50 bg-destructive/10 rounded-lg">
          <AlertCircle className="h-5 w-5 text-destructive" />
          <p className="text-sm text-destructive">
            {error instanceof Error
              ? error.message
              : "Failed to load templates"}
          </p>
          <Button
            variant="outline"
            size="sm"
            onClick={() => refetch()}
            className="ml-auto"
          >
            Retry
          </Button>
        </div>
      )}

      {/* Templates List */}
      {templates.length === 0 ? (
        <Card className="p-12 text-center">
          <p className="text-muted-foreground mb-4">
            No request templates defined yet
          </p>
          <Button onClick={() => setCreateDialogOpen(true)} variant="outline">
            <Plus className="h-4 w-4 mr-2" />
            Create your first template
          </Button>
        </Card>
      ) : (
        <div className="grid gap-4">
          {templates.map((template) => (
            <Card key={template.id} className="p-4">
              <div className="flex items-start justify-between">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-3 mb-2">
                    <Clock className="h-4 w-4 text-muted-foreground flex-shrink-0" />
                    <h3 className="font-semibold">{template.name}</h3>
                    <Badge variant="outline" className="text-xs">
                      {getDurationLabel(template)}
                    </Badge>
                  </div>

                  {template.description && (
                    <p className="text-sm text-muted-foreground mb-2 ml-7">
                      {template.description}
                    </p>
                  )}

                  <p className="text-xs text-muted-foreground mt-2 ml-7">
                    Created: {template.createdAt ? new Date(template.createdAt).toLocaleDateString() : 'N/A'}
                  </p>
                </div>

                <div className="flex gap-2 ml-4">
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() => setEditingTemplate(template)}
                  >
                    <Edit className="h-4 w-4" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() => handleDelete(template)}
                    className="text-destructive hover:text-destructive"
                  >
                    <Trash2 className="h-4 w-4 text-destructive" />
                  </Button>
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}

      {/* Dialogs */}
      <CreateTemplateDialog
        open={createDialogOpen}
        onOpenChange={setCreateDialogOpen}
        onSuccess={handleCreateSuccess}
        entityType={entityType}
      />

      {editingTemplate && (
        <EditTemplateDialog
          open={!!editingTemplate}
          onOpenChange={(open: boolean) => !open && setEditingTemplate(null)}
          template={editingTemplate}
          onSuccess={handleUpdateSuccess}
        />
      )}
    </div>
  );
}
