import { useState } from 'react';
import { Settings } from 'lucide-react';
import { useAppStore } from '@foundation/src/store/app-store';
import { useSpaces } from '@foundation/src/hooks/useSpaces';
import { Button } from '@foundation/src/components/ui/button';
import { ScrollArea } from '@foundation/src/components/ui/scroll-area';
import type { Space } from '@foundation/src/types/space';
import { SpaceCapabilitiesEditor } from './SpaceCapabilitiesEditor';

/**
 * Spaces > Capabilities tab. Lists every space with a button that opens the
 * existing SpaceCapabilitiesEditor modal — same editor wired in the canvas
 * sidebar, surfaced here for discoverability. Per spec §01: "No space
 * capabilities defined yet." empty state.
 */
export function SpaceCapabilitiesTab() {
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);
  const { data: spaces = [], isLoading } = useSpaces(selectedSiteId);
  const [editing, setEditing] = useState<Space | null>(null);

  if (!selectedSiteId) {
    return (
      <div className="rounded-2xl border bg-card p-6">
        <p className="text-muted-foreground">Please select a site to manage capabilities.</p>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <p className="text-sm text-muted-foreground">Loading spaces...</p>
      </div>
    );
  }

  if (spaces.length === 0) {
    return (
      <div className="rounded-2xl border bg-card p-6 text-center">
        <p className="text-sm text-muted-foreground">No space capabilities defined yet.</p>
      </div>
    );
  }

  return (
    <div className="h-full flex flex-col bg-card rounded-lg border">
      <div className="p-4 border-b">
        <h3 className="font-semibold">Capabilities by Space</h3>
        <p className="text-xs text-muted-foreground mt-1">
          Assign criteria values (e.g. capacity, accessibility) to individual spaces.
        </p>
      </div>
      <ScrollArea className="flex-1">
        <div className="divide-y">
          {spaces.map((space) => (
            <div key={space.id} className="flex items-center justify-between p-4">
              <div>
                <div className="font-medium">{space.name}</div>
                {space.code && (
                  <div className="text-xs text-muted-foreground">{space.code}</div>
                )}
              </div>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setEditing(space)}
              >
                <Settings className="h-4 w-4 mr-2" />
                Manage capabilities
              </Button>
            </div>
          ))}
        </div>
      </ScrollArea>

      {editing && (
        <SpaceCapabilitiesEditor
          open={!!editing}
          onOpenChange={(open) => !open && setEditing(null)}
          siteId={selectedSiteId}
          resourceId={editing.id}
          spaceName={editing.name}
        />
      )}
    </div>
  );
}
