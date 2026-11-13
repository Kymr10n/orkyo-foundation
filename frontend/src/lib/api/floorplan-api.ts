import { apiDelete, apiRawFetch, buildUrl } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import { uploadWithProgress } from '../core/upload';

export interface FloorplanMetadata {
  assetId: string;
  fileName: string;
  mimeType: string;
  fileSizeBytes: number;
  checksumSha256: string;
  widthPx: number;
  heightPx: number;
  uploadedAt: string;
  uploadedByUserId?: string;
}

interface UploadFloorplanResponse {
  success: boolean;
  metadata: FloorplanMetadata;
}

export interface FloorplanViewData {
  blobUrl: string;
  widthPx: number;
  heightPx: number;
}

export async function uploadFloorplan(
  siteId: string,
  file: File,
  onProgress?: (progress: number) => void
): Promise<FloorplanMetadata> {
  const formData = new FormData();
  formData.append('file', file);

  const response = await uploadWithProgress<UploadFloorplanResponse>(
    buildUrl(API_PATHS.siteFloorplan(siteId)), formData, { onProgress });
  return response.metadata;
}

export async function getFloorplanMetadata(siteId: string): Promise<FloorplanMetadata | null> {
  const response = await apiRawFetch(API_PATHS.siteFloorplanMetadata(siteId));

  // Backend returns 200 with "null" body when no floorplan exists
  const data = await response.json();
  return data;
}

export async function getFloorplanImageUrl(siteId: string): Promise<string> {
  const response = await apiRawFetch(API_PATHS.siteFloorplan(siteId), "GET", { cache: "default" });
  const blob = await response.blob();
  // Use data URL instead of blob URL — Firefox blocks blob: inside SVG <image>.
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onloadend = () => resolve(reader.result as string);
    reader.onerror = () => reject(new Error("Failed to read floorplan image"));
    reader.readAsDataURL(blob);
  });
}

/**
 * Returns null when no floorplan exists. The image fetch is gated on metadata so
 * a 404 from the image endpoint cannot surface as a query error for the empty case.
 */
export async function getFloorplanViewData(siteId: string): Promise<FloorplanViewData | null> {
  const metadata = await getFloorplanMetadata(siteId);
  if (!metadata) {
    return null;
  }

  const blobUrl = await getFloorplanImageUrl(siteId);
  return {
    blobUrl,
    widthPx: metadata.widthPx,
    heightPx: metadata.heightPx,
  };
}

export const deleteFloorplan = (siteId: string) =>
  apiDelete(API_PATHS.siteFloorplan(siteId));
