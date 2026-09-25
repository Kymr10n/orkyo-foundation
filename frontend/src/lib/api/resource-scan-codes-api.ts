/**
 * API client for QR sticker codes on resources (docs/qr-resource-linking-spec.md).
 *
 * A code is the decoded sticker text, stored as-is. Orkyo never opens a URL a code carries.
 */

import { apiDelete, apiGet, apiPost } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export interface ResourceScanCodeInfo {
  id: string;
  resourceId: string;
  code: string;
  createdAt: string;
}

export type ScanCodeLookupStatus = 'linked' | 'unknown' | 'type_disabled';

export interface ScanCodeResourceRef {
  id: string;
  name: string;
  resourceTypeKey: string;
  isActive: boolean;
}

export interface ScanCodeLookupResult {
  status: ScanCodeLookupStatus;
  /** Present only when `status` is `linked`. */
  resource?: ScanCodeResourceRef | null;
}

export async function lookupScanCode(code: string): Promise<ScanCodeLookupResult> {
  return apiGet<ScanCodeLookupResult>(API_PATHS.RESOURCE_SCAN_CODE_LOOKUP, { params: { code } });
}

export async function getResourceScanCodes(resourceId: string): Promise<ResourceScanCodeInfo[]> {
  return apiGet<ResourceScanCodeInfo[]>(API_PATHS.resourceScanCodes(resourceId));
}

/** `moveFromOtherResource` moves a code that another resource carries; without it the server refuses. */
export async function linkResourceScanCode(
  resourceId: string,
  code: string,
  moveFromOtherResource = false,
): Promise<ResourceScanCodeInfo> {
  return apiPost<ResourceScanCodeInfo>(API_PATHS.resourceScanCodes(resourceId), { code, moveFromOtherResource });
}

export async function unlinkResourceScanCode(resourceId: string, codeId: string): Promise<void> {
  return apiDelete(API_PATHS.resourceScanCode(resourceId, codeId));
}
