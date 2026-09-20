import { useCallback, useEffect, useState } from "react";
import {
  getFloorplanImageUrl,
  getFloorplanMetadata,
  type FloorplanMetadata,
} from "@foundation/src/lib/api/floorplan-api";
import { logger } from "@foundation/src/lib/core/logger";

export interface SiteFloorplan {
  metadata: FloorplanMetadata | null;
  /** The image, fetched with auth headers and held as a blob url. */
  blobUrl: string | null;
  /** Adopt the metadata an upload just returned. */
  setMetadata: (metadata: FloorplanMetadata) => void;
  /** Forget the plan after it was deleted. */
  clear: () => void;
}

/**
 * The floorplan image of one site, for the surfaces that draw on it: the metadata gives the
 * canvas its pixel dimensions, the blob url gives it the picture.
 */
export function useSiteFloorplan(siteId: string): SiteFloorplan {
  const [metadata, setMetadata] = useState<FloorplanMetadata | null>(null);
  const [blobUrl, setBlobUrl] = useState<string | null>(null);

  // Load floorplan metadata on mount
  useEffect(() => {
    if (siteId) {
      getFloorplanMetadata(siteId)
        .then(setMetadata)
        .catch((err: unknown) => logger.error(err));
    }
  }, [siteId]);

  // Fetch floorplan image with auth headers and create a data URL. Keyed on the metadata
  // object, not merely on whether one exists: replacing a floorplan swaps the metadata while
  // it stays truthy, and that must refetch rather than leave the previous image on the canvas.
  // No clearing here — the caller reads the url through the current metadata, so a stale blob
  // url is never rendered.
  useEffect(() => {
    if (!siteId || !metadata) return;
    let cancelled = false;
    getFloorplanImageUrl(siteId)
      .then((url) => {
        if (!cancelled) {
          setBlobUrl(url);
        }
      })
      .catch((err: unknown) => logger.error("Failed to load floorplan image:", err));
    return () => {
      cancelled = true;
    };
  }, [siteId, metadata]);

  const clear = useCallback(() => {
    setMetadata(null);
    setBlobUrl(null);
  }, []);

  return { metadata, blobUrl, setMetadata, clear };
}
