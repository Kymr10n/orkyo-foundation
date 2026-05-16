import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as ApiUtils from "../core/api-utils";
import {
  getDepartments,
  getDepartmentTree,
  getDepartment,
  createDepartment,
  updateDepartment,
  deleteDepartment,
  type DepartmentInfo,
  type DepartmentTreeNode,
  type CreateDepartmentRequest,
  type UpdateDepartmentRequest,
} from "./departments-api";

vi.mock("@foundation/src/contexts/AuthContext", () => ({
  getAuthTokenSync: () => null,
  getTenantSlugSync: () => null,
}));

vi.mock("@foundation/src/lib/core/csrf", () => ({
  getCsrfToken: () => "test-csrf-token",
  CSRF_HEADER_NAME: "X-CSRF-Token",
  isMutatingMethod: (m: string) =>
    ["POST", "PUT", "PATCH", "DELETE"].includes(m.toUpperCase()),
}));

vi.mock("@foundation/src/config/runtime", () => ({
  runtimeConfig: { apiBaseUrl: "http://localhost:5000", baseDomain: "" },
}));

vi.mock("../core/api-utils", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiUtils>();
  return {
    ...actual,
    handleApiError: vi.fn().mockImplementation(async (response: Response) => {
      const text =
        (await response.text?.()) || `Error ${(response).status}`;
      throw new Error(text);
    }),
    API_BASE_URL: "http://localhost:5000",
  };
});

const mockFetch = vi.fn();
global.fetch = mockFetch;

const mockDepartment: DepartmentInfo = {
  id: "dept-1",
  parentDepartmentId: undefined,
  name: "Engineering",
  code: "ENG",
  description: "Engineering department",
  isActive: true,
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-01-01T00:00:00Z",
};

const mockDepartmentTree: DepartmentTreeNode[] = [
  {
    id: "dept-1",
    parentDepartmentId: undefined,
    name: "Engineering",
    code: "ENG",
    description: "Engineering department",
    isActive: true,
    children: [
      {
        id: "dept-2",
        parentDepartmentId: "dept-1",
        name: "Frontend",
        code: "FE",
        description: "Frontend team",
        isActive: true,
        children: [],
      },
    ],
  },
];

describe("departments-api", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("getDepartments", () => {
    it("fetches departments without includeInactive", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve([mockDepartment]),
      });

      const result = await getDepartments();
      expect(result).toEqual([mockDepartment]);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/departments",
        expect.any(Object)
      );
    });

    it("fetches departments with includeInactive", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve([mockDepartment]),
      });

      const result = await getDepartments(true);
      expect(result).toEqual([mockDepartment]);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/departments?includeInactive=true",
        expect.any(Object)
      );
    });
  });

  describe("getDepartmentTree", () => {
    it("fetches department tree", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockDepartmentTree),
      });

      const result = await getDepartmentTree();
      expect(result).toEqual(mockDepartmentTree);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/departments/tree",
        expect.any(Object)
      );
    });

    it("fetches department tree with includeInactive", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockDepartmentTree),
      });

      const result = await getDepartmentTree(true);
      expect(result).toEqual(mockDepartmentTree);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/departments/tree?includeInactive=true",
        expect.any(Object)
      );
    });
  });

  describe("getDepartment", () => {
    it("fetches a single department by id", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockDepartment),
      });

      const result = await getDepartment("dept-1");
      expect(result).toEqual(mockDepartment);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/departments/dept-1",
        expect.any(Object)
      );
    });
  });

  describe("createDepartment", () => {
    it("creates a department", async () => {
      const request: CreateDepartmentRequest = {
        name: "New Department",
        parentDepartmentId: undefined,
        code: "NEW",
        description: "New department",
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ ...mockDepartment, ...request }),
      });

      const result = await createDepartment(request);
      expect(result.name).toBe(request.name);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/departments",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify(request),
        })
      );
    });
  });

  describe("updateDepartment", () => {
    it("updates a department", async () => {
      const request: UpdateDepartmentRequest = {
        name: "Updated Department",
        isActive: false,
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ ...mockDepartment, ...request }),
      });

      const result = await updateDepartment("dept-1", request);
      expect(result.name).toBe(request.name);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/departments/dept-1",
        expect.objectContaining({
          method: "PUT",
          body: JSON.stringify(request),
        })
      );
    });
  });

  describe("deleteDepartment", () => {
    it("deletes a department", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(undefined),
      });

      await deleteDepartment("dept-1");
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/departments/dept-1",
        expect.objectContaining({
          method: "DELETE",
        })
      );
    });
  });
});
