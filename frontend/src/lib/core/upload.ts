import { getApiHeaders, handleApiError } from './api-utils';

export interface UploadOptions {
  /** Omit for a `FormData` body: the browser then sets the type with its boundary. */
  contentType?: string;
  onProgress?: (percent: number) => void;
}

/**
 * POSTs `body` and reports how much of it has been sent. XMLHttpRequest because `fetch`
 * cannot report upload progress. A failure goes through `handleApiError`, so it carries the
 * same `ApiError` (and the same session handling) as every other call.
 */
export function uploadWithProgress<T>(
  url: string,
  body: XMLHttpRequestBodyInit,
  { contentType, onProgress }: UploadOptions = {},
): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.withCredentials = true;

    xhr.upload.addEventListener('progress', (event) => {
      if (onProgress && event.lengthComputable) onProgress(Math.round((event.loaded / event.total) * 100));
    });

    xhr.addEventListener('load', () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        try {
          resolve(JSON.parse(xhr.responseText) as T);
        } catch {
          reject(new Error('The server sent a response that could not be read.'));
        }
        return;
      }
      handleApiError(new Response(xhr.responseText, { status: xhr.status, statusText: xhr.statusText }))
        .catch((error: unknown) => reject(error instanceof Error ? error : new Error(String(error))));
    });
    xhr.addEventListener('error', () => reject(new Error('The connection was lost during the upload.')));
    xhr.addEventListener('abort', () => reject(new Error('The upload was cancelled.')));

    xhr.open('POST', url);
    for (const [name, value] of Object.entries(getApiHeaders('POST'))) {
      if (name.toLowerCase() !== 'content-type') xhr.setRequestHeader(name, value);
    }
    if (contentType) xhr.setRequestHeader('Content-Type', contentType);
    xhr.send(body);
  });
}
