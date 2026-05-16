import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as ApiUtils from "../core/api-utils";
import {
  getJobTitles,
  getJobTitle,
  createJobTitle,
  updateJobTitle,
  deleteJobTitle,
  type JobTitleInfo,
  type CreateJobTitleRequest,
  type UpdateJobTitleRequest,
} from "./job-titles-api";

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

const mockJobTitle: JobTitleInfo = {
  id: "jt-1",
  name: "Software Engineer",
  description: "Develops software",
  isActive: true,
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-01-01T00:00:00Z",
};

describe("job-titles-api", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("getJobTitles", () => {
    it("fetches job titles without includeInactive", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve([mockJobTitle]),
      });

      const result = await getJobTitles();
      expect(result).toEqual([mockJobTitle]);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/job-titles",
        expect.any(Object)
      );
    });

    it("fetches job titles with includeInactive", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve([mockJobTitle]),
      });

      const result = await getJobTitles(true);
      expect(result).toEqual([mockJobTitle]);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/job-titles?includeInactive=true",
        expect.any(Object)
      );
    });
  });

  describe("getJobTitle", () => {
    it("fetches a single job title by id", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockJobTitle),
      });

      const result = await getJobTitle("jt-1");
      expect(result).toEqual(mockJobTitle);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/job-titles/jt-1",
        expect.any(Object)
      );
    });
  });

  describe("createJobTitle", () => {
    it("creates a job title", async () => {
      const request: CreateJobTitleRequest = {
        name: "Product Manager",
        description: "Manages products",
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ ...mockJobTitle, ...request }),
      });

      const result = await createJobTitle(request);
      expect(result.name).toBe(request.name);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/job-titles",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify(request),
        })
      );
    });
  });

  describe("updateJobTitle", () => {
    it("updates a job title", async () => {
      const request: UpdateJobTitleRequest = {
        name: "Senior Software Engineer",
        isActive: false,
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ ...mockJobTitle, ...request }),
      });

      const result = await updateJobTitle("jt-1", request);
      expect(result.name).toBe(request.name);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/job-titles/jt-1",
        expect.objectContaining({
          method: "PUT",
          body: JSON.stringify(request),
        })
      );
    });
  });

  describe("deleteJobTitle", () => {
    it("deletes a job title", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(undefined),
      });

      await deleteJobTitle("jt-1");
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/job-titles/jt-1",
        expect.objectContaining({
          method: "DELETE",
        })
      );
    });
  });
});
