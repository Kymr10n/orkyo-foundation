/**
 * API client for Department CRUD operations.
 * Departments are a tenant-wide hierarchical reference list assigned to person
 * resources. They carry no scheduling/availability/capability semantics — they
 * are purely organizational.
 */

import { apiGet } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import { createCrudApi } from './create-crud-api';

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

const departmentsApi = createCrudApi<DepartmentInfo, CreateDepartmentRequest, UpdateDepartmentRequest>({
  collectionPath: API_PATHS.DEPARTMENTS,
  itemPath: API_PATHS.department,
});

export function getDepartments(includeInactive = false): Promise<DepartmentInfo[]> {
  return departmentsApi.list(includeInactive ? { includeInactive: 'true' } : undefined);
}

// Tree view is a distinct read-only collection endpoint (not part of the CRUD shape),
// so it stays explicit.
export async function getDepartmentTree(includeInactive = false): Promise<DepartmentTreeNode[]> {
  const path = includeInactive
    ? `${API_PATHS.DEPARTMENTS_TREE}?includeInactive=true`
    : API_PATHS.DEPARTMENTS_TREE;
  return apiGet<DepartmentTreeNode[]>(path);
}

export function getDepartment(id: string): Promise<DepartmentInfo> {
  return departmentsApi.get(id);
}

export function createDepartment(
  request: CreateDepartmentRequest,
): Promise<DepartmentInfo> {
  return departmentsApi.create(request);
}

export function updateDepartment(
  id: string,
  request: UpdateDepartmentRequest,
): Promise<DepartmentInfo> {
  return departmentsApi.update(id, request);
}

export function deleteDepartment(id: string): Promise<void> {
  return departmentsApi.remove(id);
}
