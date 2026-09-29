import { Maximize, ZoomIn, ZoomOut } from "lucide-react";
import { Button } from "@foundation/src/components/ui/button";
import { cn } from "@foundation/src/lib/utils";
import type { AnchoredZoom } from "@foundation/src/hooks/useAnchoredZoom";

/** Zoom out, reset and zoom in for a surface driven by `useAnchoredZoom`. */
export function ZoomControls({ zoom, className }: { zoom: AnchoredZoom; className?: string }) {
  return (
    <div className={cn("flex items-center gap-1", className)}>
      <Button
        variant="outline" size="icon" aria-label="Zoom out"
        onClick={zoom.zoomOut}
        disabled={!zoom.canZoomOut}
      >
        <ZoomOut className="h-4 w-4" />
      </Button>
      <Button variant="outline" size="icon" aria-label="Reset zoom" onClick={zoom.reset}>
        <Maximize className="h-4 w-4" />
      </Button>
      <Button
        variant="outline" size="icon" aria-label="Zoom in"
        onClick={zoom.zoomIn}
        disabled={!zoom.canZoomIn}
      >
        <ZoomIn className="h-4 w-4" />
      </Button>
    </div>
  );
}
