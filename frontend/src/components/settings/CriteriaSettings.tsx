import { useState } from 'react';
import { SettingsPageHeader } from './SettingsPageHeader';
import { Plus, Edit, Trash2, AlertCircle } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { Card } from '@foundation/src/components/ui/card';
import { Badge } from '@foundation/src/components/ui/badge';
import { Tabs, TabsList, TabsTrigger } from '@foundation/src/components/ui/tabs';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { CreateCriterionDialog } from './CreateCriterionDialog';
import { EditCriterionDialog } from './EditCriterionDialog';
import { getDataTypeColor } from '@foundation/src/lib/utils';
import type { CreateCriterionRequest, ResourceTypeKey } from '@foundation/src/types/criterion';
import type { Criterion } from '@foundation/src/types/criterion';
import { useExportHandler, useImportHandler } from '@foundation/src/hooks/useImportExport';
import { exportCriteria, importCriteria } from '@foundation/src/lib/utils/export-handlers';
import { useCriteria, useCreateCriterion, useDeleteCriterion } from '@foundation/src/hooks/useCriteria';
import { logger } from '@foundation/src/lib/core/logger';

type FilterTab = 'all' | ResourceTypeKey;

const FILTER_TABS: { value: FilterTab; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'space', label: 'Spaces' },
  { value: 'person', label: 'People' },
];

// 'tool' is intentionally kept so existing tool criteria render a correct badge.
// Remove once the tools feature is built and the filter tab is re-added.
const RESOURCE_TYPE_LABELS: Record<ResourceTypeKey, string> = {
  space: 'Spaces',
  person: 'People',
  tool: 'Tools',
};

export function CriteriaSettings() {
  const [createDialogOpen, setCreateDialogOpen] = useState(false);
  const [editingCriterion, setEditingCriterion] = useState<Criterion | null>(null);
  const [activeFilter, setActiveFilter] = useState<FilterTab>('all');

  // Use React Query for criteria data
  const { data: criteria = [], isLoading, error, refetch } = useCriteria();
  const createMutation = useCreateCriterion();
  const deleteMutation = useDeleteCriterion();

  // Handle export/import
  useExportHandler('criteria', async (format) => {
    await exportCriteria(criteria, format);
    logger.info(`Exported ${criteria.length} criteria as ${format.toUpperCase()}`);
  });

  useImportHandler('criteria', async (file, format) => {
    try {
      const importedCriteria = await importCriteria(file, format);
      if (!importedCriteria.length) {
        throw new Error('No valid criteria found in file');
      }
      // Create criteria via API - mutations auto-invalidate cache
      for (const criterion of importedCriteria) {
        await createMutation.mutateAsync(criterion as CreateCriterionRequest);
      }
      alert(`Successfully imported ${importedCriteria.length} criteria`);
    } catch (error) {
      logger.error('Import failed:', error);
      alert(error instanceof Error ? error.message : 'Failed to import criteria');
    }
  });

  const handleCreateSuccess = () => {
    // Cache is automatically invalidated by the mutation hook
    setCreateDialogOpen(false);
  };

  const handleUpdateSuccess = () => {
    // Cache is automatically invalidated by the mutation hook
    setEditingCriterion(null);
  };

  const handleDelete = async (criterion: Criterion) => {
    if (!confirm(`Delete criterion "${criterion.name}"? This action cannot be undone.`)) {
      return;
    }

    try {
      await deleteMutation.mutateAsync(criterion.id);
      // Cache is automatically invalidated by the mutation hook
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Failed to delete criterion');
    }
  };

  const filteredCriteria = activeFilter === 'all'
    ? criteria
    : criteria.filter((c) => c.resourceTypeKeys?.includes(activeFilter));

  const columns: ColumnDef<Criterion>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => (
        <span className="font-mono text-sm font-semibold">{row.original.name}</span>
      ),
    },
    {
      id: 'type',
      header: 'Type',
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
      header: 'Applies To',
      cell: ({ row }) => (
        <div className="flex flex-wrap gap-1">
          {row.original.resourceTypeKeys?.map((key) => (
            <Badge key={key} variant="outline" className="text-xs">
              {RESOURCE_TYPE_LABELS[key]}
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
      header: 'Created',
      cell: ({ row }) => (
        <span className="text-xs text-muted-foreground">
          {new Date(row.original.createdAt).toLocaleDateString()}
        </span>
      ),
    },
    {
      id: 'actions',
      header: () => null,
      size: 96,
      cell: ({ row }) => {
        const criterion = row.original;
        return (
          <div className="flex justify-end gap-1">
            <Button
              variant="ghost"
              size="icon"
              onClick={(e) => {
                e.stopPropagation();
                setEditingCriterion(criterion);
              }}
              aria-label={`Edit ${criterion.name}`}
              title="Edit criterion"
            >
              <Edit className="h-4 w-4" />
            </Button>
            <Button
              variant="ghost"
              size="icon"
              onClick={(e) => {
                e.stopPropagation();
                handleDelete(criterion);
              }}
              className="text-destructive hover:text-destructive"
              aria-label={`Delete ${criterion.name}`}
              title="Delete criterion"
            >
              <Trash2 className="h-4 w-4 text-destructive" />
            </Button>
          </div>
        );
      },
    },
  ];

  // Context-aware empty message for tabs that filter to zero rows. The truly-empty
  // case (no criteria at all) is handled by the CTA card below, not the table.
  const emptyMessage =
    activeFilter === 'all'
      ? 'No criteria match your search.'
      : `No criteria defined for ${RESOURCE_TYPE_LABELS[activeFilter]} yet. Create a criterion and mark it as applicable to ${RESOURCE_TYPE_LABELS[activeFilter]}.`;

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <p className="text-muted-foreground">Loading criteria...</p>
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
        <Button onClick={() => setCreateDialogOpen(true)}>
          <Plus className="h-4 w-4 mr-2" />
          Add Criterion
        </Button>
      </SettingsPageHeader>

      {/* Error State */}
      {error && (
        <div className="flex items-center gap-2 p-4 border border-destructive/50 bg-destructive/10 rounded-lg">
          <AlertCircle className="h-5 w-5 text-destructive" />
          <p className="text-sm text-destructive">{error instanceof Error ? error.message : 'Failed to load criteria'}</p>
          <Button variant="outline" size="sm" onClick={() => refetch()} className="ml-auto">
            Retry
          </Button>
        </div>
      )}

      {/* Filter Tabs */}
      <Tabs value={activeFilter} onValueChange={(v) => setActiveFilter(v as FilterTab)}>
        <TabsList>
          {FILTER_TABS.map(({ value, label }) => (
            <TabsTrigger key={value} value={value}>
              {label}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>

      {/* Criteria List */}
      {criteria.length === 0 ? (
        <Card className="p-12 text-center">
          <p className="text-muted-foreground mb-4">No criteria defined yet</p>
          <Button onClick={() => setCreateDialogOpen(true)} variant="outline">
            <Plus className="h-4 w-4 mr-2" />
            Create your first criterion
          </Button>
        </Card>
      ) : (
        <OrkyoDataTable
          columns={columns}
          data={filteredCriteria}
          emptyMessage={emptyMessage}
          filterColumn="name"
          filterPlaceholder="Search criteria..."
          onRowClick={(criterion) => setEditingCriterion(criterion)}
        />
      )}

      {/* Dialogs */}
      <CreateCriterionDialog
        open={createDialogOpen}
        onOpenChange={setCreateDialogOpen}
        onSuccess={handleCreateSuccess}
      />

      {editingCriterion && (
        <EditCriterionDialog
          criterion={editingCriterion}
          open={!!editingCriterion}
          onOpenChange={(open: boolean) => !open && setEditingCriterion(null)}
          onSuccess={handleUpdateSuccess}
        />
      )}
    </div>
  );
}
