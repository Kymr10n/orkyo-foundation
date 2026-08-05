import { useState, useCallback } from 'react';
import { SettingsPageHeader } from './SettingsPageHeader';
import { Plus, Edit, Trash2, AlertCircle } from 'lucide-react';
import { Alert, AlertDescription } from '@foundation/src/components/ui/alert';
import { Button } from '@foundation/src/components/ui/button';
import { Badge } from '@foundation/src/components/ui/badge';
import { EmptyState } from '@foundation/src/components/ui/EmptyState';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@foundation/src/components/ui/tooltip';
import { CriterionEditDialog } from './CriterionEditDialog';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { getDataTypeColor } from '@foundation/src/lib/utils';
import type { CreateCriterionRequest } from '@foundation/src/types/criterion';
import type { Criterion } from '@foundation/src/types/criterion';
import { useExportHandler, useImportHandler } from '@foundation/src/hooks/useImportExport';
import { exportCriteria, importCriteria } from '@foundation/src/lib/utils/export-handlers';
import {
  useCriteria,
  useCreateCriterion,
  useDeleteCriterion,
} from '@foundation/src/hooks/useCriteria';
import { qk } from '@foundation/src/lib/api/query-keys';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import { useEditQueryParam } from '@foundation/src/hooks/useEditQueryParam';
import { logger } from '@foundation/src/lib/core/logger';
import { formatDateDisplay } from '@foundation/src/lib/formatters';
import { useTableUrlState } from '@foundation/src/hooks/useTableUrlState';

export function CriteriaSettings() {
  const [createDialogOpen, setCreateDialogOpen] = useState(false);
  const [editingCriterion, setEditingCriterion] = useState<Criterion | null>(null);
  const [deletingCriterion, setDeletingCriterion] = useState<Criterion | null>(null);

  // Use React Query for criteria data
  const { data: criteria = [], isLoading, error, refetch } = useCriteria();
  const { data: resourceTypes = [] } = useResourceTypes(true);
  const createMutation = useCreateCriterion();
  const deleteMutation = useDeleteCriterion();
  const canEdit = useCanEdit();

  // A criterion can reference a type that was since deactivated or removed; fall back to
  // the raw key so the badge still says something truthful rather than "undefined".
  const labelForType = useCallback(
    (key: string) => resourceTypes.find((t) => t.key === key)?.displayName ?? key,
    [resourceTypes],
  );

  // Open the edit dialog when arriving with ?edit=<id> from global search.
  useEditQueryParam(criteria, setEditingCriterion, { ready: !isLoading });

  // Handle export/import
  useExportHandler('criteria', async (format) => {
    await exportCriteria(criteria, format);
    logger.info(`Exported ${criteria.length} criteria as ${format.toUpperCase()}`);
  }, { label: 'Criteria', description: 'Export or import criteria definitions and their data types.', formats: ['csv', 'json'] });

  useImportHandler(
    'criteria',
    async (file, format) => {
      const importedCriteria = await importCriteria(file, format);
      if (!importedCriteria.length) {
        throw new Error('No valid criteria found in file');
      }
      // Create criteria via API - mutations auto-invalidate cache
      for (const criterion of importedCriteria) {
        await createMutation.mutateAsync(criterion as CreateCriterionRequest);
      }
      return importedCriteria.length;
    },
    {
      successMessage: (count) => `Imported ${count} criterion${count === 1 ? '' : 'ia'}`,
      errorMessage: 'Failed to import criteria',
      formats: ['csv', 'json'],
      invalidates: [qk.criteria.all()],
    },
  );

  const handleDelete = (criterion: Criterion) => setDeletingCriterion(criterion);
  const handleConfirmDelete = async () => {
    if (!deletingCriterion) return;
    try {
      await deleteMutation.mutateAsync(deletingCriterion.id);
      setDeletingCriterion(null);
    } catch {
      // toast already fired centrally via useDeleteCriterion's mutation meta
    }
  };

  // Shared row actions — desktop table cell and phone card. The delete is
  // disabled (with an explaining tooltip) while the criterion is in use.
  const renderActions = (criterion: Criterion) => (
    <div className="flex justify-end gap-1">
      <Button
        variant="ghost"
        size="icon"
        disabled={!canEdit}
        onClick={(e) => {
          e.stopPropagation();
          setEditingCriterion(criterion);
        }}
        aria-label={`Edit ${criterion.name}`}
        title="Edit criterion"
      >
        <Edit className="h-4 w-4" />
      </Button>
      <TooltipProvider delayDuration={300}>
        <Tooltip>
          <TooltipTrigger asChild>
            {/* Span wrapper so the tooltip still fires while the button is disabled */}
            <span className="inline-flex">
              <Button
                variant="ghost"
                size="icon"
                disabled={criterion.inUse || !canEdit}
                onClick={(e) => {
                  e.stopPropagation();
                  handleDelete(criterion);
                }}
                className="text-destructive hover:text-destructive"
                aria-label={`Delete ${criterion.name}`}
              >
                <Trash2 className="h-4 w-4 text-destructive" />
              </Button>
            </span>
          </TooltipTrigger>
          <TooltipContent>
            {criterion.inUse
              ? 'Cannot delete: this criterion has existing values'
              : 'Delete criterion'}
          </TooltipContent>
        </Tooltip>
      </TooltipProvider>
    </div>
  );

  // Phone presentation: name + type, applies-to, description, actions trailing.
  const renderCard = (criterion: Criterion) => (
    <div className="flex items-start justify-between gap-2">
      <div className="min-w-0 space-y-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-mono text-sm font-semibold truncate">{criterion.name}</span>
          <Badge className={getDataTypeColor(criterion.dataType)}>{criterion.dataType}</Badge>
          {criterion.unit && (
            <span className="text-xs text-muted-foreground">({criterion.unit})</span>
          )}
        </div>
        {criterion.resourceTypeKeys && criterion.resourceTypeKeys.length > 0 && (
          <div className="flex flex-wrap gap-1">
            {criterion.resourceTypeKeys.map((key) => (
              <Badge key={key} variant="outline" className="text-xs">
                {labelForType(key)}
              </Badge>
            ))}
          </div>
        )}
        {criterion.description && (
          <p className="text-sm text-muted-foreground">{criterion.description}</p>
        )}
      </div>
      {renderActions(criterion)}
    </div>
  );

  const columns: ColumnDef<Criterion>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      meta: { filter: { type: 'text' } },
      cell: ({ row }) => (
        <span className="font-mono text-sm font-semibold">{row.original.name}</span>
      ),
    },
    {
      id: 'type',
      accessorFn: (r) => r.dataType,
      header: 'Type',
      meta: { filter: { type: 'enum' } },
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <Badge className={getDataTypeColor(row.original.dataType)}>
            {row.original.dataType}
          </Badge>
          {row.original.unit && (
            <span className="text-xs text-muted-foreground">({row.original.unit})</span>
          )}
        </div>
      ),
    },
    {
      id: 'appliesTo',
      accessorFn: (r) => r.resourceTypeKeys ?? [],
      header: 'Applies To',
      meta: { filter: { type: 'enum', isArray: true, getLabel: labelForType } },
      cell: ({ row }) => (
        <div className="flex flex-wrap gap-1">
          {row.original.resourceTypeKeys?.map((key) => (
            <Badge key={key} variant="outline" className="text-xs">
              {labelForType(key)}
            </Badge>
          ))}
        </div>
      ),
    },
    {
      id: 'description',
      header: 'Description',
      cell: ({ row }) => {
        const { description, enumValues } = row.original;
        if (!description && (!enumValues || enumValues.length === 0)) {
          return <span className="text-muted-foreground">—</span>;
        }
        return (
          <div className="max-w-md">
            {description && (
              <p className="text-sm text-muted-foreground truncate">{description}</p>
            )}
            {enumValues && enumValues.length > 0 && (
              <div className="mt-1 flex flex-wrap gap-1">
                {enumValues.map((value) => (
                  <Badge key={value} variant="outline" className="text-xs">
                    {value}
                  </Badge>
                ))}
              </div>
            )}
          </div>
        );
      },
    },
    {
      id: 'created',
      accessorFn: (r) => r.createdAt,
      header: 'Created',
      meta: { filter: { type: 'date' } },
      cell: ({ row }) => (
        <span className="text-xs text-muted-foreground">
          {formatDateDisplay(row.original.createdAt)}
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

  // Header sort/filter state lives in the URL: bookmarkable, shareable, Back-safe.
  const tableUrlState = useTableUrlState('criteria', columns);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <p className="text-muted-foreground">Loading criteria…</p>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <SettingsPageHeader
        title="Criteria Definitions"
        description="Define reusable criteria that can be used as space capabilities or utilization requirements. These criteria enable automatic validation during utilization."
      >
        <Button onClick={() => setCreateDialogOpen(true)} disabled={!canEdit}>
          <Plus className="h-4 w-4 mr-2" />
          Add Criterion
        </Button>
      </SettingsPageHeader>

      {/* Error State */}
      {error && (
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertDescription className="flex items-center justify-between gap-2">
            <span>{error instanceof Error ? error.message : 'Failed to load criteria'}</span>
            <Button variant="outline" size="sm" onClick={() => refetch()}>
              Try again
            </Button>
          </AlertDescription>
        </Alert>
      )}

      {/* Criteria List */}
      {criteria.length === 0 ? (
        <EmptyState
          message="No criteria defined yet"
          action={
            <Button onClick={() => setCreateDialogOpen(true)} variant="outline" disabled={!canEdit}>
              <Plus className="h-4 w-4 mr-2" />
              Create your first criterion
            </Button>
          }
        />
      ) : (
        <OrkyoDataTable
        {...tableUrlState}
          columns={columns}
          data={criteria}
          emptyMessage="No criteria match your search."
          onRowClick={(criterion) => setEditingCriterion(criterion)}
          renderCard={renderCard}
        />
      )}

      {/* Dialogs */}
      <CriterionEditDialog
        criterion={null}
        open={createDialogOpen}
        onOpenChange={setCreateDialogOpen}
      />

      {editingCriterion && (
        <CriterionEditDialog
          criterion={editingCriterion}
          open={!!editingCriterion}
          onOpenChange={(open: boolean) => !open && setEditingCriterion(null)}
        />
      )}

      <ConfirmDialog
        open={!!deletingCriterion}
        onOpenChange={(open) => !open && setDeletingCriterion(null)}
        title={`Delete "${deletingCriterion?.name}"?`}
        description="This action cannot be undone."
        confirmLabel="Delete"
        destructive
        isPending={deleteMutation.isPending}
        onConfirm={handleConfirmDelete}
      />
    </div>
  );
}
