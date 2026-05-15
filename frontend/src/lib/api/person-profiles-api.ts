/**
 * API client for Person Profile operations
 */

import { apiGet, apiPut, apiPost, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

// Re-export PersonProfileInfo from the models
export interface PersonProfileInfo {
  resourceId: string;
  email?: string;
  jobTitle?: string;
  department?: string;
  linkedUserId?: string;
  notes?: string;
  createdAt: string;
  updatedAt: string;
}

export interface UpsertPersonProfileRequest {
  email?: string;
  jobTitle?: string;
  department?: string;
  notes?: string;
}

export interface LinkUserToPersonProfileRequest {
  userId: string;
}

/**
 * Get a person profile by resource ID
 */
export async function getPersonProfile(resourceId: string): Promise<PersonProfileInfo> {
  return apiGet<PersonProfileInfo>(API_PATHS.personProfile(resourceId));
}

/**
 * Upsert a person profile
 */
export async function upsertPersonProfile(
  resourceId: string,
  request: UpsertPersonProfileRequest
): Promise<PersonProfileInfo> {
  return apiPut<PersonProfileInfo>(API_PATHS.personProfile(resourceId), request);
}

/**
 * Link a user to a person profile
 */
export async function linkUserToPersonProfile(
  resourceId: string,
  request: LinkUserToPersonProfileRequest
): Promise<void> {
  return apiPost<void>(API_PATHS.personProfileLink(resourceId), request);
}

/**
 * Unlink a user from a person profile
 */
export async function unlinkUserFromPersonProfile(resourceId: string): Promise<void> {
  return apiDelete(API_PATHS.personProfileLink(resourceId));
}

export function usePersonProfilesApi() {
  return {
    getPersonProfile,
    upsertPersonProfile,
    linkUserToPersonProfile,
    unlinkUserFromPersonProfile,
  };
}
