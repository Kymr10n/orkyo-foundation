/**
 * Site-admin feedback triage API client.
 *
 * These endpoints require the site-admin role in Keycloak. The public submit client lives in
 * `feedback-api.ts`; this one is for the AdminPage Feedback tab (read + triage).
 */

import { apiGet, apiPatch } from '../core/api-client';
import { API_BASE_URL } from '../core/api-utils';

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
  filters?: { status?: string; type?: string },
): Promise<{ items: FeedbackSummary[]; total: number }> {
  const params: Record<string, string> = {};
  if (filters?.status) params.status = filters.status;
  if (filters?.type) params.type = filters.type;
  return apiGet<{ items: FeedbackSummary[]; total: number }>(BASE, { params });
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
