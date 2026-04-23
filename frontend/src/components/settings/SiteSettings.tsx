import { Button } from "@foundation/src/components/ui/button";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { Card } from "@foundation/src/components/ui/card";
import { type Site } from "@foundation/src/lib/api/site-api";
import type { CreateSiteRequest } from "@foundation/src/types/site";
import { AlertCircle, Edit, MapPin, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { CreateSiteDialog } from "./CreateSiteDialog";
import { EditSiteDialog } from "./EditSiteDialog";
import { useExportHandler, useImportHandler } from '@foundation/src/hooks/useImportExport';
import { exportSites, importSites } from '@foundation/src/lib/utils/export-handlers';
import { useSites, useDeleteSite, useCreateSite } from "@foundation/src/hooks/useSites";
import { logger } from "@foundation/src/lib/core/logger";

export function SiteSettings() {
  const [createDialogOpen, setCreateDialogOpen] = useState(false);
  const [editingSite, setEditingSite] = useState<Site | null>(null);

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

  const handleDelete = async (site: Site) => {
    if (!confirm(`Delete site "${site.name}"? This action cannot be undone.`)) {
      return;
    }

    try {
      await deleteMutation.mutateAsync(site.id);
    } catch (err) {
      alert(err instanceof Error ? err.message : "Failed to delete site");
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <p className="text-muted-foreground">Loading sites...</p>
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
        <div className="flex items-center gap-2 p-4 border border-destructive/50 bg-destructive/10 rounded-lg">
          <AlertCircle className="h-5 w-5 text-destructive" />
          <p className="text-sm text-destructive">
            {error instanceof Error ? error.message : "Failed to load sites"}
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
        <div className="grid gap-4">
          {sites.map((site) => (
            <Card key={site.id} className="p-4">
              <div className="flex items-start justify-between">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-3 mb-2">
                    <MapPin className="h-4 w-4 text-muted-foreground flex-shrink-0" />
                    <h3 className="font-semibold">{site.name}</h3>
                    <span className="text-xs text-muted-foreground font-mono">
                      [{site.code}]
                    </span>
                  </div>

                  {site.description && (
                    <p className="text-sm text-muted-foreground mb-2 ml-7">
                      {site.description}
                    </p>
                  )}

                  {site.address && (
                    <p className="text-xs text-muted-foreground ml-7">
                      📍 {site.address}
                    </p>
                  )}

                  <p className="text-xs text-muted-foreground mt-2 ml-7">
                    Created: {new Date(site.createdAt).toLocaleDateString()}
                  </p>
                </div>

                <div className="flex gap-2 ml-4">
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() => setEditingSite(site)}
                  >
                    <Edit className="h-4 w-4" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() => handleDelete(site)}
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
    </div>
  );
}
