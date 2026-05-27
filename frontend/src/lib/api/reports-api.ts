import { apiGet, apiPost } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import type { components } from './generated/openapi';

export type ReportDefinition = components['schemas']['ReportDefinition'];
export type ReportEmbedTokenResult = components['schemas']['ReportEmbedTokenResult'];

export async function getReports(): Promise<ReportDefinition[]> {
  return apiGet<ReportDefinition[]>(API_PATHS.REPORTS);
}

export async function createReportEmbedToken(reportKey: string): Promise<ReportEmbedTokenResult> {
  return apiPost<ReportEmbedTokenResult>(API_PATHS.reportEmbedToken(reportKey), {});
}
