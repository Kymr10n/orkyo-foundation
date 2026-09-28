
import { Badge } from "@foundation/src/components/ui/badge";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { Button } from "@foundation/src/components/ui/button";
import { createTemplate } from "@foundation/src/lib/api/template-api";
import type { Template, CreateTemplateRequest } from "@foundation/src/types/templates";
import type { DurationUnit } from "@foundation/src/types/requests";
import { DURATION_TO_MINUTES } from "@foundation/src/domain/constants";
import { qk } from "@foundation/src/lib/api/query-keys";
import { useDeleteTemplate, useTemplates } from "@foundation/src/hooks/useTemplates";
import { Clock, Pencil, Plus, Trash2 } from "lucide-react";
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import { useState } from "react";
import { TemplateDialogBase } from "./TemplateDialogBase";
import { useExportHandler, useImportHandler } from '@foundation/src/hooks/useImportExport';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { useEditQueryParam } from '@foundation/src/hooks/useEditQueryParam';
import { exportTemplates, importTemplates } from '@foundation/src/lib/utils/export-handlers';
import { logger } from '@foundation/src/lib/core/logger';
import { formatDateDisplay } from '@foundation/src/lib/formatters';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { RowActions } from '@foundation/src/components/ui/RowActions';
import { useTableUrlState } from '@foundation/src/hooks/useTableUrlState';
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";

interface TemplateSettingsProps {
  entityType?: 'request' | 'space' | 'group';
}

export function TemplateSettings({ entityType = 'request' }: TemplateSettingsProps) {
  const canEdit = useCanEdit();
  const [createDialogOpen, setCreateDialogOpen] = useState(false);
  const [editingTemplate, setEditingTemplate] =
    useState<Template | null>(null);
  const [deletingTemplate, setDeletingTemplate] = useState<Template | null>(null);

  // Load templates with React Query
  const {
    data: templates = [],
    isLoading,
    error,
    refetch,
  } = useTemplates(entityType);

  // Open the edit dialog when arriving with ?edit=<id> from global search.
  useEditQueryParam(templates, setEditingTemplate, { ready: !isLoading });

  const deleteMutation = useDeleteTemplate(entityType);

  // Handle export/import
  useExportHandler('templates', async (format) => {
    await exportTemplates(templates, format);
    logger.info(`Exported ${templates.length} templates as ${format.toUpperCase()}`);
  }, { label: 'Request templates', description: 'Export or import request templates.', formats: ['csv', 'json'] });

  useImportHandler('templates', async (file, format) => {
    const importedTemplates = await importTemplates(file, format);
    if (!importedTemplates.length) {
      throw new Error('No valid templates found in file');
    }
    // Create templates via API
    for (const template of importedTemplates) {
      await createTemplate(template as CreateTemplateRequest);
    }
    return importedTemplates.length;
  }, {
    successMessage: (count) => `Imported ${count} template${count === 1 ? '' : 's'}`,
    errorMessage: 'Import failed',
    formats: ['csv', 'json'],
    invalidates: [qk.templates(entityType)],
  });

  const handleCreateSuccess = () => {
    setCreateDialogOpen(false);
  };

  const handleUpdateSuccess = () => {
    setEditingTemplate(null);
  };

  const handleDelete = (template: Template) => setDeletingTemplate(template);
  const handleConfirmDelete = async () => {
    if (!deletingTemplate) return;
    try {
      await deleteMutation.mutateAsync(deletingTemplate.id);
      setDeletingTemplate(null);
    } catch {
      // error toast fired centrally via the mutation's meta (MutationCache)
    }
  };

  const getDurationLabel = (template: Template) => {
    if (!template.durationUnit) return 'No duration set';
    return `${template.durationValue} ${template.durationUnit}`;
  };

  // Shared row actions — desktop table cell and phone card.
  const renderActions = (template: Template) => (
    <RowActions
      triggerLabel={`Actions for ${template.name}`}
      actions={[
        { label: "Edit", icon: Pencil, onSelect: () => setEditingTemplate(template), disabled: !canEdit },
        { label: "Delete", icon: Trash2, onSelect: () => handleDelete(template), disabled: !canEdit, destructive: true },
      ]}
    />
  );

  const columns: ColumnDef<Template>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      meta: { filter: { type: 'text' } },
      cell: ({ row }) => (
        <span className="font-semibold">{row.original.name}</span>
      ),
    },
    {
      id: 'duration',
      // Sort/filter on raw minutes, not the "2 hours" display string. Templates
      // without a duration count as 0 so they sort together at one end.
      accessorFn: (r) =>
        r.durationUnit ? (r.durationValue ?? 0) * (DURATION_TO_MINUTES[r.durationUnit as DurationUnit] ?? 1) : 0,
      header: 'Duration',
      meta: { filter: { type: 'number' } },
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <Clock className="h-4 w-4 text-muted-foreground shrink-0" />
          <Badge variant="outline" className="text-xs">{getDurationLabel(row.original)}</Badge>
        </div>
      ),
    },
    {
      id: 'description',
      accessorFn: (r) => r.description ?? '',
      header: 'Description',
      meta: { filter: { type: 'text' } },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground truncate max-w-md">
          {row.original.description ?? '—'}
        </span>
      ),
    },
    {
      id: 'created',
      accessorFn: (r) => r.createdAt ?? '',
      header: 'Created',
      meta: { filter: { type: 'date' } },
      cell: ({ row }) => (
        <span className="text-xs text-muted-foreground">
          {row.original.createdAt ? formatDateDisplay(row.original.createdAt) : 'N/A'}
        </span>
      ),
    },
    {
      id: 'actions',
      header: () => null,
      size: 96,
      cell: ({ row }) => renderActions(row.original),
    },
  ];

  const tableUrlState = useTableUrlState('templates', columns);

  // Phone presentation: name, duration badge + description, actions trailing.
  const renderCard = (template: Template) => (
    <div className="flex items-start justify-between gap-2">
      <div className="min-w-0 space-y-1">
        <span className="font-semibold truncate block">{template.name}</span>
        <div className="flex items-center gap-2">
          <Clock className="h-4 w-4 text-muted-foreground shrink-0" />
          <Badge variant="outline" className="text-xs">{getDurationLabel(template)}</Badge>
        </div>
        {template.description && (
          <p className="text-sm text-muted-foreground">{template.description}</p>
        )}
      </div>
      {renderActions(template)}
    </div>
  );

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <LoadingSpinner inline size="xs" muted message="Loading request templates…" />
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
        <Button onClick={() => setCreateDialogOpen(true)} disabled={!canEdit}>
          <Plus className="h-4 w-4 mr-2" />
          Add Template
        </Button>
      </SettingsPageHeader>

      <OrkyoDataTable
        {...tableUrlState}
        columns={columns}
        data={templates}
        error={error ? error.message || "Failed to load templates" : null}
        onRetry={() => refetch()}
        emptyMessage={templates.length === 0 ? "No request templates defined yet" : undefined}
        emptyAction={
          templates.length === 0 && (
            <Button onClick={() => setCreateDialogOpen(true)} variant="outline" disabled={!canEdit}>
              <Plus className="h-4 w-4 mr-2" />
              Create your first template
            </Button>
          )
        }
        renderCard={renderCard}
        onRowClick={(template) => setEditingTemplate(template)}
      />

      {/* Dialogs */}
      <TemplateDialogBase
        open={createDialogOpen}
        onOpenChange={setCreateDialogOpen}
        template={null}
        onSuccess={handleCreateSuccess}
        entityType={entityType}
      />

      {editingTemplate && (
        <TemplateDialogBase
          open={!!editingTemplate}
          onOpenChange={(open: boolean) => !open && setEditingTemplate(null)}
          template={editingTemplate}
          onSuccess={handleUpdateSuccess}
        />
      )}

      <ConfirmDialog
        open={!!deletingTemplate}
        onOpenChange={(open) => !open && setDeletingTemplate(null)}
        title={`Delete "${deletingTemplate?.name}"?`}
        description="This action cannot be undone."
        confirmLabel="Delete"
        destructive
        isPending={deleteMutation.isPending}
        onConfirm={handleConfirmDelete}
      />
    </div>
  );
}
