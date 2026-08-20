import { useState } from 'react';
import { Factory, Truck } from 'lucide-react';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@foundation/src/components/ui/card';
import {
  AlertDialog,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@foundation/src/components/ui/alert-dialog';
import { Badge } from '@foundation/src/components/ui/badge';
import { Button } from '@foundation/src/components/ui/button';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { Switch } from '@foundation/src/components/ui/switch';
import { SettingsPageHeader } from './SettingsPageHeader';
import {
  useActivateCatalogType,
  useDeactivateCatalogType,
  usePurgeCatalogType,
  useResourceTypeCatalog,
} from '@foundation/src/hooks/useResourceTypeCatalog';
import { resourceTypeIcon } from '@foundation/src/components/resources/resource-type-icon';
import type { CatalogEntry } from '@foundation/src/lib/api/resource-type-catalog-api';

/**
 * Switches for the pre-configured manufacturing types. Separate from the Resource Types
 * CRUD tab on purpose: this is opt-in over a fixed catalog, that is free-form definition.
 * Activated entries become ordinary, fully editable tenant types.
 *
 * No role gating inside this page: it is mounted only under RequireTenantAdmin, and the
 * API behind it is admin-write, so everyone who can see it can use all of it.
 */
export function TypeCatalogSettings() {
  const { data: entries = [], isLoading, error, refetch } = useResourceTypeCatalog();
  const activate = useActivateCatalogType();
  const deactivate = useDeactivateCatalogType();
  const purge = usePurgeCatalogType();

  // Switching OFF an in-use entry needs a decision first: hide (keep data) or delete
  // everything. `deciding` holds that dialog's entry; `purging` the second-step confirm.
  const [deciding, setDeciding] = useState<CatalogEntry | null>(null);
  const [purging, setPurging] = useState<CatalogEntry | null>(null);

  const pendingAny = activate.isPending || deactivate.isPending || purge.isPending;

  const onToggle = (entry: CatalogEntry, checked: boolean) => {
    if (checked) {
      activate.mutate(entry.key);
      return;
    }
    if (entry.resourceCount > 0 || entry.requestTargetCount > 0) {
      setDeciding(entry);
      return;
    }
    deactivate.mutate(entry.key);
  };

  const errorMsg =
    error instanceof Error ? error.message : error ? 'Failed to load the type catalog' : null;

  const renderGroup = (
    category: CatalogEntry['category'],
    Icon: typeof Factory,
    title: string,
    description: string,
  ) => (
    <Card>
      <CardHeader className="pb-3 md:pb-3">
        <CardTitle className="flex items-center gap-2 text-base">
          <Icon className="h-4 w-4" />
          {title}
        </CardTitle>
        <CardDescription>{description}</CardDescription>
      </CardHeader>
      <CardContent>
        <div className="divide-y">
          {entries
            .filter((e) => e.category === category)
            .map((entry) => {
              const EntryIcon = resourceTypeIcon(entry.icon);
              const renamed =
                entry.tenantDisplayName != null && entry.tenantDisplayName !== entry.displayName;
              const inUse = entry.resourceCount > 0 || entry.requestTargetCount > 0;
              return (
                <div key={entry.key} className="flex items-center justify-between gap-4 py-3">
                  <div className="flex min-w-0 items-start gap-3">
                    <EntryIcon className="mt-0.5 h-5 w-5 shrink-0 text-muted-foreground" />
                    <div className="min-w-0 space-y-0.5">
                      <div className="flex items-center gap-2">
                        <span className="text-sm font-medium">{entry.displayNamePlural}</span>
                        {renamed && (
                          <Badge variant="outline" className="text-xs">
                            renamed to {entry.tenantDisplayName}
                          </Badge>
                        )}
                      </div>
                      <p className="text-xs text-muted-foreground">{entry.description}</p>
                      <p className="text-xs text-muted-foreground">
                        {entry.fieldLabels.length} preset fields
                        {inUse && ` · ${entry.resourceCount} resources`}
                      </p>
                    </div>
                  </div>
                  <Switch
                    checked={entry.state === 'active'}
                    onCheckedChange={(checked) => onToggle(entry, checked)}
                    disabled={pendingAny}
                    aria-label={`Activate ${entry.displayNamePlural}`}
                  />
                </div>
              );
            })}
        </div>
      </CardContent>
    </Card>
  );

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="Type catalog"
        description="Pre-configured manufacturing resource types. Switch one on and it becomes an ordinary type with industry-typical fields — rename it, change its fields, or remove it like any other."
      />

      {isLoading && <p className="text-sm text-muted-foreground">Loading the catalog…</p>}
      {errorMsg && (
        <div className="space-y-2">
          <p className="text-sm text-destructive">{errorMsg}</p>
          <Button variant="outline" size="sm" onClick={() => refetch()}>
            Retry
          </Button>
        </div>
      )}

      {!isLoading && !errorMsg && (
        <>
          {renderGroup(
            'Stationary',
            Factory,
            'Stationary equipment',
            'Machines and stations with a fixed place on the floorplan.',
          )}
          {renderGroup(
            'Mobile',
            Truck,
            'Mobile assets',
            'People, tools and vehicles that move between stations.',
          )}
        </>
      )}

      {/* Step 1: the in-use entry needs a decision — hide (data survives) or delete everything. */}
      <AlertDialog open={!!deciding} onOpenChange={(open) => !open && setDeciding(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              Deactivate {deciding?.tenantDisplayName ?? deciding?.displayName}?
            </AlertDialogTitle>
            <AlertDialogDescription>
              This type is in use: {deciding?.resourceCount} resources
              {deciding && deciding.requestTargetCount > 0
                ? ` and ${deciding.requestTargetCount} request targets`
                : ''}{' '}
              reference it. Hiding keeps all data and the type can be switched back on later.
              Deleting removes the type together with its resources, their assignments and booking
              history, its groups, and its request targets.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <Button variant="outline" onClick={() => setDeciding(null)}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              onClick={() => {
                setPurging(deciding);
                setDeciding(null);
              }}
            >
              Delete everything…
            </Button>
            <Button
              onClick={() => {
                if (!deciding) return;
                deactivate.mutate(deciding.key, { onSuccess: () => setDeciding(null) });
              }}
              loading={deactivate.isPending}
              disabled={deactivate.isPending}
            >
              Hide type
            </Button>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* Step 2: the destructive path gets its own explicit confirm. */}
      <ConfirmDialog
        open={!!purging}
        onOpenChange={(open) => !open && setPurging(null)}
        title={`Delete "${purging?.tenantDisplayName ?? purging?.displayName}" and all its data?`}
        description={`This permanently deletes ${purging?.resourceCount} resources with their assignments and booking history, every group of this type, and ${purging?.requestTargetCount} request targets. This cannot be undone.`}
        confirmLabel="Delete everything"
        destructive
        isPending={purge.isPending}
        onConfirm={() => {
          if (!purging) return;
          purge.mutate(purging.key, { onSuccess: () => setPurging(null) });
        }}
      />
    </div>
  );
}
