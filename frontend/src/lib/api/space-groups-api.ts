import type {
  CreateSpaceGroupRequest,
  SpaceGroup,
  UpdateSpaceGroupRequest,
} from "@foundation/src/types/spaceGroup";
import { apiDelete, apiGet, apiPost, apiPut } from "../core/api-client";
import { logger } from "@foundation/src/lib/core/logger";

export async function getSpaceGroups(): Promise<SpaceGroup[]> {
  try {
    return await apiGet<SpaceGroup[]>("/api/groups");
  } catch (error) {
    // Return empty array for expected errors (404, etc.)
    logger.warn("Failed to fetch space groups:", error);
    return [];
  }
}

export async function createSpaceGroup(
  data: CreateSpaceGroupRequest,
): Promise<SpaceGroup> {
  return apiPost<SpaceGroup>("/api/groups", data);
}

export async function updateSpaceGroup(
  id: string,
  data: UpdateSpaceGroupRequest,
): Promise<SpaceGroup> {
  return apiPut<SpaceGroup>(`/api/groups/${id}`, data);
}

export async function deleteSpaceGroup(id: string): Promise<void> {
  return apiDelete(`/api/groups/${id}`);
}
