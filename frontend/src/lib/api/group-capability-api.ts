import type { CriterionValue } from "@/types/criterion";
import { apiDelete, apiGet, apiPost } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

interface GroupCapability {
  id: string;
  groupId: string;
  criterionId: string;
  value: CriterionValue;
  createdAt: string;
  updatedAt: string;
  criterion?: {
    id: string;
    name: string;
    dataType: string;
    unit?: string;
  };
}

interface CreateGroupCapabilityRequest {
  criterionId: string;
  value: CriterionValue;
}

export async function getGroupCapabilities(groupId: string): Promise<GroupCapability[]> {
  return apiGet<GroupCapability[]>(API_PATHS.groupCapabilities(groupId));
}

export async function addGroupCapability(
  groupId: string,
  request: CreateGroupCapabilityRequest
): Promise<GroupCapability> {
  return apiPost<GroupCapability>(
    API_PATHS.groupCapabilities(groupId),
    request
  );
}

export async function deleteGroupCapability(
  groupId: string,
  capabilityId: string
): Promise<void> {
  return apiDelete(API_PATHS.groupCapability(groupId, capabilityId));
}
