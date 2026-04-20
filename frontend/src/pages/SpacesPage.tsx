import { SpaceManagementPanel } from '@/components/spaces/SpaceManagementPanel';
import { useAppStore } from '@/store/app-store';
import { useSearchParams } from 'react-router-dom';

export function SpacesPage() {
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);
  const [searchParams] = useSearchParams();
  const editSpaceId = searchParams.get('edit');

  if (!selectedSiteId) {
    return (
      <div className="rounded-2xl border bg-card p-6">
        <h1 className="text-xl font-semibold mb-4">Spaces</h1>
        <p className="text-muted-foreground">Please select a site to manage spaces.</p>
      </div>
    );
  }

  return (
    <div className="h-full flex flex-col">
      <SpaceManagementPanel siteId={selectedSiteId} editSpaceId={editSpaceId} className="flex-1" />
    </div>
  );
}
