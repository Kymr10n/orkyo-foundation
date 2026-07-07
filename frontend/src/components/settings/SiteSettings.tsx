import { Button } from "@foundation/src/components/ui/button";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { Card } from "@foundation/src/components/ui/card";
import { type Site } from "@foundation/src/lib/api/site-api";
import type { CreateSiteRequest } from "@foundation/src/types/site";
import { AlertCircle, Edit, MapPin, Plus, Trash2 } from "lucide-react";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { useState } from "react";
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import { CreateSiteDialog } from "./CreateSiteDialog";
import { EditSiteDialog } from "./EditSiteDialog";
import { useExportHandler, useImportHandler } from '@foundation/src/hooks/useImportExport';
import { exportSites, importSites } from '@foundation/src/lib/utils/export-handlers';
import { useSites, useDeleteSite, useCreateSite } from "@foundation/src/hooks/useSites";
import { logger } from "@foundation/src/lib/core/logger";
import { OrkyoDataTable, type ColumnDef } from "@foundation/src/components/ui/OrkyoDataTable";

export function SiteSettings() {
  const [createDialogOpen, setCreateDialogOpen] = useState(false);
  const [editingSite, setEditingSite] = useState<Site | null>(null);
  const [deletingSite, setDeletingSite] = useState<Site | null>(null);

  // Load sites with React Query
  const {
    data: sites = [],
    isLoading,
    error,
    refetch,
  } = useSites();

  // Mutations
  const deleteMutation = useDeleteSite();
  const createMutation = useCreateSite();

  // Handle export/import
  useExportHandler('sites', async (format) => {
    await exportSites(sites, format);
    logger.info(`Exported ${sites.length} sites as ${format.toUpperCase()}`);
  });

  useImportHandler('sites', async (file, format) => {
    try {
      const importedSites = await importSites(file, format);
      if (!importedSites.length) {
        throw new Error('No valid sites found in file');
      }
      // Create sites via API
      for (const site of importedSites) {
        await createMutation.mutateAsync(site as CreateSiteRequest);
      }
      alert(`Successfully imported ${importedSites.length} sites`);
    } catch (error) {
      logger.error('Import failed:', error);
      alert(error instanceof Error ? error.message : 'Failed to import sites');
    }
  });

  const handleCreateSuccess = (_newSite: Site) => {
    setCreateDialogOpen(false);
  };

  const handleUpdateSuccess = (_updatedSite: Site) => {
    setEditingSite(null);
  };

  const handleConfirmDelete = async () => {
    if (!deletingSite) return;
    try {
      await deleteMutation.mutateAsync(deletingSite.id);
      setDeletingSite(null);
    } catch {
      // Error toast is surfaced by the mutation's meta handler; the dialog stays
      // open so the user can retry.
    }
  };

  // Shared row actions — desktop table cell and phone card. Stop propagation so
  // a tap doesn't also trigger the row/card edit-onClick.
  const renderActions = (site: Site) => (
    <div className="flex justify-end gap-1">
      <Button
        variant="ghost"
        size="icon"
        onClick={(e) => { e.stopPropagation(); setEditingSite(site); }}
        aria-label={`Edit ${site.name}`}
        title="Edit site"
      >
        <Edit className="h-4 w-4" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        onClick={(e) => { e.stopPropagation(); setDeletingSite(site); }}
        className="text-destructive hover:text-destructive"
        aria-label={`Delete ${site.name}`}
        title="Delete site"
      >
        <Trash2 className="h-4 w-4 text-destructive" />
      </Button>
    </div>
  );

  const columns: ColumnDef<Site>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <MapPin className="h-4 w-4 text-muted-foreground shrink-0" />
          <span className="font-semibold">{row.original.name}</span>
          <span className="text-xs text-muted-foreground font-mono">[{row.original.code}]</span>
        </div>
      ),
    },
    {
      id: 'address',
      header: 'Address',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{row.original.address ?? '—'}</span>
      ),
    },
    {
      id: 'description',
      header: 'Description',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground truncate max-w-sm">
          {row.original.description ?? '—'}
        </span>
      ),
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
      cell: ({ row }) => renderActions(row.original),
    },
  ];

  // Phone presentation: name/code, address + description, actions trailing.
  const renderCard = (site: Site) => (
    <div className="flex items-start justify-between gap-2">
      <div className="min-w-0 space-y-1">
        <div className="flex items-center gap-2">
          <MapPin className="h-4 w-4 text-muted-foreground shrink-0" />
          <span className="font-semibold truncate">{site.name}</span>
          <span className="text-xs text-muted-foreground font-mono">[{site.code}]</span>
        </div>
        <p className="text-sm text-muted-foreground">{site.address ?? '—'}</p>
        {site.description && (
          <p className="text-sm text-muted-foreground">{site.description}</p>
        )}
      </div>
      {renderActions(site)}
    </div>
  );

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <p className="text-muted-foreground">Loading sites…</p>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <SettingsPageHeader
        title="Sites"
        description="Manage physical sites and locations. Each site can contain multiple spaces and serve as an organizational unit for utilization planning."
      >
        <Button onClick={() => setCreateDialogOpen(true)}>
          <Plus className="h-4 w-4 mr-2" />
          Add Site
        </Button>
      </SettingsPageHeader>

      {/* Error State */}
      {error && (
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertDescription className="flex items-center justify-between gap-2">
            <span>{error instanceof Error ? error.message : "Failed to load sites"}</span>
            <Button variant="outline" size="sm" onClick={() => refetch()}>
              Try again
            </Button>
          </AlertDescription>
        </Alert>
      )}

      {/* Sites List */}
      {sites.length === 0 ? (
        <Card className="p-12 text-center">
          <p className="text-muted-foreground mb-4">No sites defined yet</p>
          <Button onClick={() => setCreateDialogOpen(true)} variant="outline">
            <Plus className="h-4 w-4 mr-2" />
            Create your first site
          </Button>
        </Card>
      ) : (
        <OrkyoDataTable
          columns={columns}
          data={sites}
          filterColumn="name"
          filterPlaceholder="Search sites..."
          onRowClick={(site) => setEditingSite(site)}
          renderCard={renderCard}
        />
      )}

      {/* Dialogs */}
      <CreateSiteDialog
        open={createDialogOpen}
        onOpenChange={setCreateDialogOpen}
        onSuccess={handleCreateSuccess}
      />

      {editingSite && (
        <EditSiteDialog
          site={editingSite}
          open={!!editingSite}
          onOpenChange={(open: boolean) => !open && setEditingSite(null)}
          onSuccess={handleUpdateSuccess}
        />
      )}

      <ConfirmDialog
        open={!!deletingSite}
        onOpenChange={(open) => !open && setDeletingSite(null)}
        title="Delete site"
        description={
          deletingSite
            ? `Delete site "${deletingSite.name}"? This action cannot be undone.`
            : ''
        }
        confirmLabel="Delete"
        destructive
        isPending={deleteMutation.isPending}
        onConfirm={handleConfirmDelete}
      />
    </div>
  );
}
