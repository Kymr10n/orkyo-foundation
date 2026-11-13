import { getApiHeaders, handleApiError } from './api-utils';
import { takeSseEvents } from './sse';

export interface UploadOptions {
  /** Omit for a `FormData` body: the browser then sets the type with its boundary. */
  contentType?: string;
  onProgress?: (percent: number) => void;
  /**
   * For a route that answers with an event stream: called with each event the server sends
   * while it works. The `result` and `error` events are not passed on: they settle the promise.
   */
  onEvent?: (name: string, data: unknown) => void;
}

const LOST_CONNECTION = 'The connection was lost before the server finished.';

/**
 * POSTs `body` and reports how much of it has been sent. XMLHttpRequest because `fetch`
 * cannot report upload progress. A failure goes through `handleApiError`, so it carries the
 * same `ApiError` (and the same session handling) as every other call.
 *
 * The answer is either JSON, or an event stream that ends with a `result` event (the value
 * the promise resolves with) or an `error` event (a problem document, handled like the body
 * of a failed request).
 */
export function uploadWithProgress<T>(
  url: string,
  body: XMLHttpRequestBodyInit,
  { contentType, onProgress, onEvent }: UploadOptions = {},
): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.withCredentials = true;

    const fail = (response: Response): void => {
      handleApiError(response)
        .catch((error: unknown) => reject(error instanceof Error ? error : new Error(String(error))));
    };

    // An event stream is read as it grows: `consumed` is how much of it has been parsed.
    let consumed = 0;
    let last: { name: string; data: string } | undefined;
    const readEvents = () => {
      const { events, rest } = takeSseEvents(xhr.responseText.slice(consumed));
      consumed = xhr.responseText.length - rest.length;
      for (const event of events) {
        if (event.name === 'result' || event.name === 'error') last = event;
        else onEvent?.(event.name, JSON.parse(event.data));
      }
    };
    const isEventStream = () => xhr.getResponseHeader('Content-Type')?.startsWith('text/event-stream') ?? false;

    xhr.upload.addEventListener('progress', (event) => {
      if (onProgress && event.lengthComputable) onProgress(Math.round((event.loaded / event.total) * 100));
    });
    xhr.addEventListener('progress', () => {
      if (isEventStream()) readEvents();
    });

    xhr.addEventListener('load', () => {
      if (xhr.status < 200 || xhr.status >= 300) {
        fail(new Response(xhr.responseText, { status: xhr.status, statusText: xhr.statusText }));
        return;
      }
      try {
        if (!isEventStream()) {
          resolve(JSON.parse(xhr.responseText) as T);
          return;
        }
        readEvents();
        if (last?.name === 'result') resolve(JSON.parse(last.data) as T);
        else if (last) fail(new Response(last.data, { status: (JSON.parse(last.data) as { status: number }).status }));
        else reject(new Error(LOST_CONNECTION));
      } catch {
        reject(new Error('The server sent a response that could not be read.'));
      }
    });
    xhr.addEventListener('error', () => reject(new Error(consumed > 0 ? LOST_CONNECTION : 'The connection was lost during the upload.')));
    xhr.addEventListener('abort', () => reject(new Error('The upload was cancelled.')));

    xhr.open('POST', url);
    for (const [name, value] of Object.entries(getApiHeaders('POST'))) {
      if (name.toLowerCase() !== 'content-type') xhr.setRequestHeader(name, value);
    }
    if (contentType) xhr.setRequestHeader('Content-Type', contentType);
    xhr.send(body);
  });
}
