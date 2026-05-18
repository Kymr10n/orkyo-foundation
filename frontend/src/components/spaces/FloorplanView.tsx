import { useSearchParams } from 'react-router-dom';
import { useAppStore } from '@foundation/src/store/app-store';
import { SpaceManagementPanel } from './SpaceManagementPanel';

/**
 * Floorplan tab content under /spaces/floorplan. Wraps the existing
 * SpaceManagementPanel (canvas + space sidebar) so the established floorplan
 * UX is preserved verbatim. A future refactor can strip the embedded
 * sidebar in favor of the standalone SpaceListView once the canvas drawing
 * flow no longer depends on the in-panel selection state.
 */
export function FloorplanView() {
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);
  const [searchParams] = useSearchParams();
  const editResourceId = searchParams.get('edit');

  if (!selectedSiteId) {
    return (
      <div className="rounded-2xl border bg-card p-6">
        <p className="text-muted-foreground">Please select a site to manage the floorplan.</p>
      </div>
    );
  }

  return (
    <div className="h-full flex flex-col">
      <SpaceManagementPanel siteId={selectedSiteId} editResourceId={editResourceId} className="flex-1" />
    </div>
  );
}
