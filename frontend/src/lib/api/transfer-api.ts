/**
 * Tenant data transfer: download the organization's data as one archive, and load an
 * archive in place of the organization's data. See docs/tenant-transfer-spec.md.
 */
import { apiRawFetch, buildUrl } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import { uploadWithProgress } from '../core/upload';

const ARCHIVE_CONTENT_TYPE = 'application/zip';
const FALLBACK_FILE_NAME = 'orkyo-transfer.orkyoexport';

/** A reason the server gives for refusing an archive; `code` is one of the `transfer_*` codes. */
export interface TransferBlocker {
  code: string;
  message: string;
}

export interface TransferValidationResult {
  sourceEdition?: string | null;
  appVersion?: string | null;
  exportedAt: string;
  sourceTenantSlug: string;
  /** Empty when the archive can be imported. */
  blockers: TransferBlocker[];
  /** Row count per table. */
  tables: Record<string, number>;
  /** Archive users that are a user of this organization, by id or by email. */
  matchedUsers: number;
  /** Emails of archive users with no match here. An import keeps them as inactive placeholders. */
  unmatchedEmails: string[];
}

export interface TransferImportResult {
  rows: number;
  matchedUsers: number;
  placeholderUsers: number;
}

export interface TransferArchiveDownload {
  blob: Blob;
  fileName: string;
}

/** Downloads the organization's archive. The server names the file. */
export async function exportTenantArchive(): Promise<TransferArchiveDownload> {
  const response = await apiRawFetch(API_PATHS.ADMIN.TRANSFER_EXPORT);
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const fileName = /filename="?([^";]+)"?/i.exec(disposition)?.[1] ?? FALLBACK_FILE_NAME;
  return { blob: await response.blob(), fileName };
}

/** Asks the server whether `archive` can be imported here. Changes nothing. */
export function validateTenantArchive(
  archive: File,
  onProgress?: (percent: number) => void,
): Promise<TransferValidationResult> {
  return uploadArchive(buildUrl(API_PATHS.ADMIN.TRANSFER_IMPORT_VALIDATE), archive, onProgress);
}

/**
 * Replaces the organization's data with `archive`. `confirmation` is the organization's
 * identifier, as the admin typed it.
 */
export function importTenantArchive(
  archive: File,
  confirmation: string,
  onProgress?: (percent: number) => void,
): Promise<TransferImportResult> {
  return uploadArchive(buildUrl(API_PATHS.ADMIN.TRANSFER_IMPORT, { confirmation }), archive, onProgress);
}

/** Sends the archive as the request body: the file itself, not a form. */
function uploadArchive<T>(url: string, archive: File, onProgress?: (percent: number) => void): Promise<T> {
  return uploadWithProgress<T>(url, archive, { contentType: ARCHIVE_CONTENT_TYPE, onProgress });
}
