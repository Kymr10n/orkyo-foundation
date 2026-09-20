/**
 * Site-admin feedback triage API client.
 *
 * These endpoints require the site-admin role in Keycloak. The public submit client lives in
 * `feedback-api.ts`; this one is for the AdminPage Feedback tab (read + triage).
 */

import { apiGet, apiPatch } from '../core/api-client';
import { API_BASE_URL } from '../core/api-utils';
import { normalizePagedResult, type PagedResult } from '../core/paged-result';

export type FeedbackStatus = 'new' | 'reviewed' | 'resolved' | 'wont_fix';
export type FeedbackType = 'bug' | 'feature' | 'question' | 'other';

export interface FeedbackSummary {
  id: string;
  feedbackType: FeedbackType;
  title: string;
  status: FeedbackStatus;
  tenantName: string | null;
  submitterEmail: string | null;
  createdAt: string;
}

export interface FeedbackDetail extends FeedbackSummary {
  description: string | null;
  pageUrl: string | null;
  userAgent: string | null;
  adminNotes: string | null;
  githubIssueUrl: string | null;
  updatedAt: string;
}

export interface UpdateFeedbackRequest {
  status?: FeedbackStatus;
  adminNotes?: string;
  githubIssueUrl?: string;
}

const BASE = `${API_BASE_URL}/api/admin/feedback`;

export async function getFeedback(
  filters?: { status?: string; type?: string; page?: number; pageSize?: number },
): Promise<PagedResult<FeedbackSummary>> {
  const params: Record<string, string> = {};
  if (filters?.status) params.status = filters.status;
  if (filters?.type) params.type = filters.type;
  if (filters?.page) params.page = String(filters.page);
  if (filters?.pageSize) params.pageSize = String(filters.pageSize);
  // Same tolerance as the resources list, through the same function: this endpoint changed its
  // params and its envelope in the same release, so a new client against an old backend gets
  // page 1 of the old shape rather than an error. Remove in the release after 0.26.0.
  const response = await apiGet<PagedResult<FeedbackSummary> & { total?: number }>(BASE, { params });
  return normalizePagedResult(response, filters?.pageSize ?? response.items?.length ?? 0);
}

export async function getFeedbackItem(id: string): Promise<FeedbackDetail> {
  return apiGet<FeedbackDetail>(`${BASE}/${id}`);
}

export async function updateFeedback(
  id: string,
  data: UpdateFeedbackRequest,
): Promise<FeedbackDetail> {
  return apiPatch<FeedbackDetail>(`${BASE}/${id}`, data);
}
