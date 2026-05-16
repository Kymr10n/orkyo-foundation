/**
 * API client for Job Title CRUD operations.
 * Job titles are tenant-wide reference data assigned to person resources.
 */

import { apiGet, apiPost, apiPut, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export interface JobTitleInfo {
  id: string;
  name: string;
  description?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateJobTitleRequest {
  name: string;
  description?: string;
}

export interface UpdateJobTitleRequest {
  name?: string;
  description?: string;
  isActive?: boolean;
}

export async function getJobTitles(includeInactive = false): Promise<JobTitleInfo[]> {
  const path = includeInactive
    ? `${API_PATHS.JOB_TITLES}?includeInactive=true`
    : API_PATHS.JOB_TITLES;
  return apiGet<JobTitleInfo[]>(path);
}

export async function getJobTitle(id: string): Promise<JobTitleInfo> {
  return apiGet<JobTitleInfo>(API_PATHS.jobTitle(id));
}

export async function createJobTitle(request: CreateJobTitleRequest): Promise<JobTitleInfo> {
  return apiPost<JobTitleInfo>(API_PATHS.JOB_TITLES, request);
}

export async function updateJobTitle(
  id: string,
  request: UpdateJobTitleRequest,
): Promise<JobTitleInfo> {
  return apiPut<JobTitleInfo>(API_PATHS.jobTitle(id), request);
}

export async function deleteJobTitle(id: string): Promise<void> {
  return apiDelete(API_PATHS.jobTitle(id));
}
