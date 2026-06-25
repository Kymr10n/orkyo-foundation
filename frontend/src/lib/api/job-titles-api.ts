/**
 * API client for Job Title CRUD operations.
 * Job titles are tenant-wide reference data assigned to person resources.
 */

import { API_PATHS } from '../core/api-paths';
import { createCrudApi } from './create-crud-api';

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

const jobTitlesApi = createCrudApi<JobTitleInfo, CreateJobTitleRequest, UpdateJobTitleRequest>({
  collectionPath: API_PATHS.JOB_TITLES,
  itemPath: API_PATHS.jobTitle,
});

export function getJobTitles(includeInactive = false): Promise<JobTitleInfo[]> {
  return jobTitlesApi.list(includeInactive ? { includeInactive: 'true' } : undefined);
}

export function getJobTitle(id: string): Promise<JobTitleInfo> {
  return jobTitlesApi.get(id);
}

export function createJobTitle(request: CreateJobTitleRequest): Promise<JobTitleInfo> {
  return jobTitlesApi.create(request);
}

export function updateJobTitle(
  id: string,
  request: UpdateJobTitleRequest,
): Promise<JobTitleInfo> {
  return jobTitlesApi.update(id, request);
}

export function deleteJobTitle(id: string): Promise<void> {
  return jobTitlesApi.remove(id);
}
