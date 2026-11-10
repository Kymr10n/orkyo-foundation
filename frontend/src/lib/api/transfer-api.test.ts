import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { exportTenantArchive, importTenantArchive, validateTenantArchive } from './transfer-api';
import * as apiClient from '../core/api-client';
import { ApiError } from '../core/api-utils';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client', async () => {
  const actual = await vi.importActual<typeof apiClient>('../core/api-client');
  return { ...actual, apiRawFetch: vi.fn() };
});

/** The slice of XMLHttpRequest the upload uses, driven by the test. */
class FakeXhr {
  static last: FakeXhr;
  status = 0;
  statusText = '';
  responseText = '';
  withCredentials = false;
  method = '';
  url = '';
  body: unknown;
  headers: Record<string, string> = {};
  upload = { addEventListener: (_: string, listener: (e: ProgressEvent) => void) => { this.onProgress = listener; } };
  private listeners: Record<string, () => void> = {};
  private onProgress?: (e: ProgressEvent) => void;

  constructor() { FakeXhr.last = this; }
  addEventListener(type: string, listener: () => void) { this.listeners[type] = listener; }
  open(method: string, url: string) { this.method = method; this.url = url; }
  setRequestHeader(name: string, value: string) { this.headers[name] = value; }
  send(body: unknown) { this.body = body; }

  progress(loaded: number, total: number) {
    this.onProgress?.({ lengthComputable: true, loaded, total } as ProgressEvent);
  }
  respond(status: number, body: unknown) {
    this.status = status;
    this.responseText = typeof body === 'string' ? body : JSON.stringify(body);
    this.listeners.load();
  }
  fail(type: 'error' | 'abort') { this.listeners[type](); }
}

const archive = new File(['zip-bytes'], 'org.orkyoexport');

describe('transfer-api', () => {
  beforeEach(() => vi.stubGlobal('XMLHttpRequest', FakeXhr));
  afterEach(() => vi.unstubAllGlobals());

  it('exportTenantArchive returns the blob under the file name the server gives', async () => {
    vi.mocked(apiClient.apiRawFetch).mockResolvedValue(new Response('zip', {
      headers: { 'Content-Disposition': 'attachment; filename="orkyo-transfer-acme-20261110-120000.orkyoexport"' },
    }));

    const download = await exportTenantArchive();

    expect(apiClient.apiRawFetch).toHaveBeenCalledWith(API_PATHS.ADMIN.TRANSFER_EXPORT);
    expect(download.fileName).toBe('orkyo-transfer-acme-20261110-120000.orkyoexport');
    expect(await download.blob.text()).toBe('zip');
  });

  it('exportTenantArchive falls back to a generic name when the server names none', async () => {
    vi.mocked(apiClient.apiRawFetch).mockResolvedValue(new Response('zip'));

    expect((await exportTenantArchive()).fileName).toBe('orkyo-transfer.orkyoexport');
  });

  it('validateTenantArchive sends the file itself as a zip body and reports progress', async () => {
    const onProgress = vi.fn();
    const pending = validateTenantArchive(archive, onProgress);
    const xhr = FakeXhr.last;

    xhr.progress(25, 100);
    xhr.respond(200, { blockers: [], matchedUsers: 2 });

    await expect(pending).resolves.toEqual({ blockers: [], matchedUsers: 2 });
    expect(xhr.method).toBe('POST');
    expect(new URL(xhr.url).pathname).toBe(API_PATHS.ADMIN.TRANSFER_IMPORT_VALIDATE);
    expect(xhr.body).toBe(archive);
    expect(xhr.headers['Content-Type']).toBe('application/zip');
    expect(xhr.withCredentials).toBe(true);
    expect(onProgress).toHaveBeenCalledWith(25);
  });

  it('importTenantArchive passes the confirmation as a query parameter', async () => {
    const pending = importTenantArchive(archive, 'my-org');
    FakeXhr.last.respond(200, { rows: 10, matchedUsers: 1, placeholderUsers: 0 });

    await expect(pending).resolves.toEqual({ rows: 10, matchedUsers: 1, placeholderUsers: 0 });
    const url = new URL(FakeXhr.last.url);
    expect(url.pathname).toBe(API_PATHS.ADMIN.TRANSFER_IMPORT);
    expect(url.searchParams.get('confirmation')).toBe('my-org');
  });

  it('a refusal becomes an ApiError with the server reason and code', async () => {
    const pending = importTenantArchive(archive, 'my-org');
    FakeXhr.last.respond(422, {
      code: 'transfer_version_mismatch',
      detail: 'The archive comes from an older schema version.',
    });

    const error = await pending.catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(422);
    expect((error as ApiError).code).toBe('transfer_version_mismatch');
    expect((error as ApiError).message).toBe('The archive comes from an older schema version.');
  });

  it('a success body that is not JSON, a lost connection and a cancel all reject', async () => {
    const unreadable = validateTenantArchive(archive);
    FakeXhr.last.respond(200, 'not json');
    await expect(unreadable).rejects.toThrow('could not be read');

    const lost = validateTenantArchive(archive);
    FakeXhr.last.fail('error');
    await expect(lost).rejects.toThrow('connection was lost');

    const cancelled = validateTenantArchive(archive);
    FakeXhr.last.fail('abort');
    await expect(cancelled).rejects.toThrow('cancelled');
  });
});
