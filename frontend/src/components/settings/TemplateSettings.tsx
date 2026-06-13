
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
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { useState } from "react";
import { CreateTemplateDialog } from "./CreateTemplateDialog";
import { EditTemplateDialog } from "./EditTemplateDialog";
import { useExportHandler, useImportHandler } from '@foundation/src/hooks/useImportExport';
import { exportTemplates, importTemplates } from '@foundation/src/lib/utils/export-handlers';
import { logger } from '@foundation/src/lib/core/logger';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';

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

  const columns: ColumnDef<Template>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => (
        <span className="font-semibold">{row.original.name}</span>
      ),
    },
    {
      id: 'duration',
      header: 'Duration',
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <Clock className="h-4 w-4 text-muted-foreground shrink-0" />
          <Badge variant="outline" className="text-xs">{getDurationLabel(row.original)}</Badge>
        </div>
      ),
    },
    {
      id: 'description',
      header: 'Description',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground truncate max-w-md">
          {row.original.description ?? '—'}
        </span>
      ),
    },
    {
      id: 'created',
      header: 'Created',
      cell: ({ row }) => (
        <span className="text-xs text-muted-foreground">
          {row.original.createdAt ? new Date(row.original.createdAt).toLocaleDateString() : 'N/A'}
        </span>
      ),
    },
    {
      id: 'actions',
      header: () => null,
      size: 96,
      cell: ({ row }) => {
        const template = row.original;
        return (
          <div className="flex justify-end gap-1">
            <Button
              variant="ghost"
              size="icon"
              onClick={(e) => { e.stopPropagation(); setEditingTemplate(template); }}
              aria-label={`Edit ${template.name}`}
              title="Edit template"
            >
              <Edit className="h-4 w-4" />
            </Button>
            <Button
              variant="ghost"
              size="icon"
              onClick={(e) => { e.stopPropagation(); handleDelete(template); }}
              className="text-destructive hover:text-destructive"
              aria-label={`Delete ${template.name}`}
              title="Delete template"
            >
              <Trash2 className="h-4 w-4 text-destructive" />
            </Button>
          </div>
        );
      },
    },
  ];

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
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertDescription className="flex items-center justify-between gap-2">
            <span>{error instanceof Error ? error.message : "Failed to load templates"}</span>
            <Button variant="outline" size="sm" onClick={() => refetch()}>
              Retry
            </Button>
          </AlertDescription>
        </Alert>
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
        <OrkyoDataTable
          columns={columns}
          data={templates}
          filterColumn="name"
          filterPlaceholder="Search templates..."
          onRowClick={(template) => setEditingTemplate(template)}
        />
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
