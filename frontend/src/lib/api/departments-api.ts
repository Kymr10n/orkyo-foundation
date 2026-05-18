/**
 * API client for Department CRUD operations.
 * Departments are a tenant-wide hierarchical reference list assigned to person
 * resources. They carry no scheduling/availability/capability semantics — they
 * are purely organizational.
 */

import { apiGet, apiPost, apiPut, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export interface DepartmentInfo {
  id: string;
  parentDepartmentId?: string;
  name: string;
  code?: string;
  description?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface DepartmentTreeNode {
  id: string;
  parentDepartmentId?: string;
  name: string;
  code?: string;
  description?: string;
  isActive: boolean;
  children: DepartmentTreeNode[];
}

export interface CreateDepartmentRequest {
  name: string;
  parentDepartmentId?: string | null;
  code?: string;
  description?: string;
}

export interface UpdateDepartmentRequest {
  name?: string;
  /**
   * When `changeParent` is true, this value (including null for "make root")
   * is honored. Otherwise the existing parent is preserved.
   */
  parentDepartmentId?: string | null;
  changeParent?: boolean;
  code?: string;
  description?: string;
  isActive?: boolean;
}

export async function getDepartments(includeInactive = false): Promise<DepartmentInfo[]> {
  const path = includeInactive
    ? `${API_PATHS.DEPARTMENTS}?includeInactive=true`
    : API_PATHS.DEPARTMENTS;
  return apiGet<DepartmentInfo[]>(path);
}

export async function getDepartmentTree(includeInactive = false): Promise<DepartmentTreeNode[]> {
  const path = includeInactive
    ? `${API_PATHS.DEPARTMENTS_TREE}?includeInactive=true`
    : API_PATHS.DEPARTMENTS_TREE;
  return apiGet<DepartmentTreeNode[]>(path);
}

export async function getDepartment(id: string): Promise<DepartmentInfo> {
  return apiGet<DepartmentInfo>(API_PATHS.department(id));
}

export async function createDepartment(
  request: CreateDepartmentRequest,
): Promise<DepartmentInfo> {
  return apiPost<DepartmentInfo>(API_PATHS.DEPARTMENTS, request);
}

export async function updateDepartment(
  id: string,
  request: UpdateDepartmentRequest,
): Promise<DepartmentInfo> {
  return apiPut<DepartmentInfo>(API_PATHS.department(id), request);
}

export async function deleteDepartment(id: string): Promise<void> {
  return apiDelete(API_PATHS.department(id));
}
