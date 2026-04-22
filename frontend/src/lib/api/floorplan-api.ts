import { apiDelete, apiRawFetch } from '../core/api-client';
import { API_BASE_URL, getApiHeaders } from '../core/api-utils';
import { API_PATHS } from '../core/api-paths';

export interface FloorplanMetadata {
  imagePath: string;
  mimeType: string;
  fileSizeBytes: number;
  widthPx: number;
  heightPx: number;
  uploadedAt: string;
  uploadedByUserId?: string;
}

interface UploadFloorplanResponse {
  success: boolean;
  metadata: FloorplanMetadata;
}

interface FloorplanViewData {
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

  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.withCredentials = true;

    if (onProgress) {
      xhr.upload.addEventListener('progress', (e) => {
        if (e.lengthComputable) {
          const progress = Math.round((e.loaded / e.total) * 100);
          onProgress(progress);
        }
      });
    }

    xhr.addEventListener('load', () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        try {
          const response: UploadFloorplanResponse = JSON.parse(xhr.responseText);
          resolve(response.metadata);
        } catch {
          reject(new Error('Invalid response format'));
        }
      } else {
        try {
          const error = JSON.parse(xhr.responseText);
          reject(new Error(error.message || 'Upload failed'));
        } catch {
          reject(new Error(`Upload failed with status ${xhr.status}`));
        }
      }
    });

    xhr.addEventListener('error', () => {
      reject(new Error('Network error during upload'));
    });

    xhr.addEventListener('abort', () => {
      reject(new Error('Upload cancelled'));
    });

    const uploadUrl = `${API_BASE_URL}${API_PATHS.siteFloorplan(siteId)}`;
    xhr.open('POST', uploadUrl);
    
    // Set headers from getApiHeaders() but exclude Content-Type for multipart/form-data
    const headers = getApiHeaders('POST');
    Object.entries(headers).forEach(([key, value]) => {
      // Don't set Content-Type - browser will set it automatically with boundary for FormData
      if (key.toLowerCase() !== 'content-type') {
        xhr.setRequestHeader(key, value);
      }
    });
    
    xhr.send(formData);
  });
}

export async function getFloorplanMetadata(siteId: string): Promise<FloorplanMetadata | null> {
  const response = await apiRawFetch(API_PATHS.siteFloorplanMetadata(siteId));

  // Backend returns 200 with "null" body when no floorplan exists
  const data = await response.json();
  return data;
}

export async function fetchFloorplanImageUrl(siteId: string): Promise<string> {
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
export async function fetchFloorplanViewData(siteId: string): Promise<FloorplanViewData | null> {
  const metadata = await getFloorplanMetadata(siteId);
  if (!metadata) {
    return null;
  }

  const blobUrl = await fetchFloorplanImageUrl(siteId);
  return {
    blobUrl,
    widthPx: metadata.widthPx,
    heightPx: metadata.heightPx,
  };
}

export const deleteFloorplan = (siteId: string) =>
  apiDelete(API_PATHS.siteFloorplan(siteId));
