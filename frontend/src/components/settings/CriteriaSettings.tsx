import { useState } from 'react';
import { SettingsPageHeader } from './SettingsPageHeader';
import { Plus, Edit, Trash2, AlertCircle } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { Card } from '@foundation/src/components/ui/card';
import { Badge } from '@foundation/src/components/ui/badge';
import { Tabs, TabsList, TabsTrigger } from '@foundation/src/components/ui/tabs';
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
  { value: 'tool', label: 'Tools' },
];

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
      {filteredCriteria.length === 0 ? (
        <Card className="p-12 text-center">
          {activeFilter === 'all' ? (
            <>
              <p className="text-muted-foreground mb-4">No criteria defined yet</p>
              <Button onClick={() => setCreateDialogOpen(true)} variant="outline">
                <Plus className="h-4 w-4 mr-2" />
                Create your first criterion
              </Button>
            </>
          ) : (
            <p className="text-muted-foreground">
              No criteria defined for {RESOURCE_TYPE_LABELS[activeFilter as ResourceTypeKey]} yet.
              Create a criterion and mark it as applicable to {RESOURCE_TYPE_LABELS[activeFilter as ResourceTypeKey]}.
            </p>
          )}
        </Card>
      ) : (
        <div className="grid gap-4">
          {filteredCriteria.map((criterion) => (
            <Card key={criterion.id} className="p-4">
              <div className="flex items-start justify-between">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-3 mb-2">
                    <h3 className="font-semibold font-mono text-sm">{criterion.name}</h3>
                    <Badge className={getDataTypeColor(criterion.dataType)}>
                      {criterion.dataType}
                    </Badge>
                    {criterion.unit && (
                      <span className="text-xs text-muted-foreground">({criterion.unit})</span>
                    )}
                    {criterion.resourceTypeKeys?.map((key) => (
                      <Badge key={key} variant="outline" className="text-xs">
                        {RESOURCE_TYPE_LABELS[key]}
                      </Badge>
                    ))}
                  </div>

                  {criterion.description && (
                    <p className="text-sm text-muted-foreground mb-2">
                      {criterion.description}
                    </p>
                  )}

                  {criterion.enumValues && criterion.enumValues.length > 0 && (
                    <div className="flex flex-wrap gap-1 mt-2">
                      {criterion.enumValues.map((value) => (
                        <Badge key={value} variant="outline" className="text-xs">
                          {value}
                        </Badge>
                      ))}
                    </div>
                  )}

                  <p className="text-xs text-muted-foreground mt-2">
                    Created: {new Date(criterion.createdAt).toLocaleDateString()}
                  </p>
                </div>

                <div className="flex gap-2 ml-4">
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() => setEditingCriterion(criterion)}
                  >
                    <Edit className="h-4 w-4" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() => handleDelete(criterion)}
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
