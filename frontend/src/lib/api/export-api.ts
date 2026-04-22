import { apiPost } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

// ============================================================================
// Types
// ============================================================================

interface ExportRequest {
  siteIds?: string[];
  includeMasterData?: boolean;
  includePlanningData?: boolean;
}

interface ExportPayload {
  schemaVersion: string;
  provenance: ExportProvenance;
  data: ExportData;
}

interface ExportProvenance {
  exportTimestamp: string;
  tenantSlug: string;
  siteIds?: string[];
  schemaVersion: string;
}

interface ExportData {
  sites?: unknown[];
  criteria?: unknown[];
  spaceGroups?: unknown[];
  templates?: unknown[];
  requests?: unknown[];
}

// ============================================================================
// API Functions
// ============================================================================

export async function exportTenantData(request: ExportRequest = {}): Promise<ExportPayload> {
  return apiPost<ExportPayload>(API_PATHS.ADMIN.EXPORT, request);
}
