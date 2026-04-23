import { type Space } from '@foundation/src/types/space';
import { Button } from '@foundation/src/components/ui/button';
import { ScrollArea } from '@foundation/src/components/ui/scroll-area';
import { cn } from '@foundation/src/lib/utils';
import { Square, Pentagon, Edit, Trash2, Settings } from 'lucide-react';

interface SpaceListProps {
  spaces: Space[];
  selectedSpaceId?: string | null;
  onSpaceSelect: (spaceId: string) => void;
  onSpaceEdit?: (space: Space) => void;
  onSpaceDelete?: (spaceId: string) => void;
  onCapabilitiesEdit?: (space: Space) => void;
  isLoading?: boolean;
}

export function SpaceList({ 
  spaces, 
  selectedSpaceId, 
  onSpaceSelect, 
  onSpaceEdit,
  onSpaceDelete,
  onCapabilitiesEdit,
  isLoading 
}: SpaceListProps) {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <p className="text-sm text-muted-foreground">Loading spaces...</p>
      </div>
    );
  }

  if (spaces.length === 0) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-center">
          <p className="text-sm text-muted-foreground mb-2">No spaces created yet</p>
          <p className="text-xs text-muted-foreground">Draw a rectangle or polygon on the floorplan</p>
        </div>
      </div>
    );
  }

  return (
    <ScrollArea className="h-full">
      <div className="space-y-2 p-4">
        {spaces.map((space) => {
          const isSelected = selectedSpaceId === space.id;
          const geometryIcon = space.geometry?.type === 'rectangle' ? Square : Pentagon;
          const GeometryIcon = geometryIcon;

          return (
            <div
              key={space.id}
              className={cn(
                'p-3 rounded-lg border bg-card cursor-pointer transition-colors hover:bg-accent',
                isSelected && 'border-primary bg-accent'
              )}
              onClick={() => onSpaceSelect(space.id)}
            >
              <div className="flex items-start justify-between gap-2">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2 mb-1">
                    <GeometryIcon className="h-3.5 w-3.5 text-muted-foreground flex-shrink-0" />
                    <span className="font-medium text-sm truncate">{space.name}</span>
                  </div>
                  {space.code && (
                    <p className="text-xs text-muted-foreground font-mono">{space.code}</p>
                  )}
                  {space.description && (
                    <p className="text-xs text-muted-foreground mt-1 line-clamp-2">
                      {space.description}
                    </p>
                  )}
                  {space.geometry && (
                    <p className="text-xs text-muted-foreground mt-1">
                      {space.geometry.type} · {space.geometry.coordinates.length} points
                    </p>
                  )}
                </div>
                <div className="flex flex-col gap-1">
                  {onCapabilitiesEdit && (
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7"
                      onClick={(e) => {
                        e.stopPropagation();
                        onCapabilitiesEdit(space);
                      }}
                      title="Edit Capabilities"
                    >
                      <Settings className="h-3.5 w-3.5" />
                    </Button>
                  )}
                  {onSpaceEdit && (
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7"
                      onClick={(e) => {
                        e.stopPropagation();
                        onSpaceEdit(space);
                      }}
                      title="Edit Space"
                    >
                      <Edit className="h-3.5 w-3.5" />
                    </Button>
                  )}
                  {onSpaceDelete && (
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7 text-destructive hover:text-destructive"
                      onClick={(e) => {
                        e.stopPropagation();
                        if (confirm(`Delete space "${space.name}"?`)) {
                          onSpaceDelete(space.id);
                        }
                      }}
                      title="Delete Space"
                    >
                      <Trash2 className="h-3.5 w-3.5 text-destructive" />
                    </Button>
                  )}
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </ScrollArea>
  );
}
