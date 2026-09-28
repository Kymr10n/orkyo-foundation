import { Button } from "@foundation/src/components/ui/button";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { type Site } from "@foundation/src/lib/api/site-api";
import type { CreateSiteRequest } from "@foundation/src/types/site";
import { MapPin, Pencil, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import { SiteEditDialog } from "./SiteEditDialog";
import { useExportHandler, useImportHandler } from '@foundation/src/hooks/useImportExport';
import { exportSites, importSites } from '@foundation/src/lib/utils/export-handlers';
import { useSites, useDeleteSite, useCreateSite } from "@foundation/src/hooks/useSites";
import { useEditQueryParam } from "@foundation/src/hooks/useEditQueryParam";
import { qk } from "@foundation/src/lib/api/query-keys";
import { logger } from "@foundation/src/lib/core/logger";
import { formatDateDisplay } from "@foundation/src/lib/formatters";
import { OrkyoDataTable, type ColumnDef } from "@foundation/src/components/ui/OrkyoDataTable";
import { RowActions } from "@foundation/src/components/ui/RowActions";
import { useTableUrlState } from '@foundation/src/hooks/useTableUrlState';
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";

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

  // Open the edit dialog when arriving with ?edit=<id> from global search.
  useEditQueryParam(sites, setEditingSite, { ready: !isLoading });

  // Mutations
  const deleteMutation = useDeleteSite();
  const createMutation = useCreateSite();

  // Handle export/import
  useExportHandler('sites', async (format) => {
    await exportSites(sites, format);
    logger.info(`Exported ${sites.length} sites as ${format.toUpperCase()}`);
  }, { label: 'Sites', description: 'Export or import site configurations and properties.', formats: ['csv', 'json'] });

  useImportHandler(
    'sites',
    async (file, format) => {
      const importedSites = await importSites(file, format);
      if (!importedSites.length) {
        throw new Error('No valid sites found in file');
      }
      for (const site of importedSites) {
        await createMutation.mutateAsync(site as CreateSiteRequest);
      }
      return importedSites.length;
    },
    {
      successMessage: (count) => `Imported ${count} sites`,
      errorMessage: 'Failed to import sites',
      formats: ['csv', 'json'],
      invalidates: [qk.sites.list()],
    },
  );

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

  // Shared row actions — desktop table cell and phone card.
  const renderActions = (site: Site) => (
    <RowActions
      triggerLabel={`Actions for ${site.name}`}
      actions={[
        { label: "Edit", icon: Pencil, onSelect: () => setEditingSite(site) },
        { label: "Delete", icon: Trash2, onSelect: () => setDeletingSite(site), destructive: true },
      ]}
    />
  );

  const columns: ColumnDef<Site>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      meta: { filter: { type: 'text' } },
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
      accessorFn: (r) => r.address ?? '',
      header: 'Address',
      meta: { filter: { type: 'text' } },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{row.original.address ?? '—'}</span>
      ),
    },
    {
      id: 'description',
      accessorFn: (r) => r.description ?? '',
      header: 'Description',
      meta: { filter: { type: 'text' } },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground truncate max-w-sm">
          {row.original.description ?? '—'}
        </span>
      ),
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
  const tableUrlState = useTableUrlState('sites', columns);

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
        <LoadingSpinner inline size="xs" muted message="Loading sites…" />
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

      <OrkyoDataTable
        {...tableUrlState}
        columns={columns}
        data={sites}
        error={error}
        errorFallback="Failed to load sites"
        onRetry={() => void refetch()}
        noDataMessage="No sites defined yet"
        noDataAction={
          <Button onClick={() => setCreateDialogOpen(true)} variant="outline">
            <Plus className="h-4 w-4 mr-2" />
            Create your first site
          </Button>
        }
        onRowClick={(site) => setEditingSite(site)}
        renderCard={renderCard}
      />

      {/* Dialogs */}
      <SiteEditDialog
        site={null}
        open={createDialogOpen}
        onOpenChange={setCreateDialogOpen}
      />

      {editingSite && (
        <SiteEditDialog
          site={editingSite}
          open={!!editingSite}
          onOpenChange={(open: boolean) => !open && setEditingSite(null)}
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
