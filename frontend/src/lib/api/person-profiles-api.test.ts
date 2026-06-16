import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as ApiUtils from "../core/api-utils";
import {
  getPersonProfile,
  getPersonJobTitles,
  upsertPersonProfile,
  linkUserToPersonProfile,
  unlinkUserFromPersonProfile,
  type PersonProfileInfo,
  type UpsertPersonProfileRequest,
  type LinkUserToPersonProfileRequest,
} from "./person-profiles-api";

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

const mockPersonProfile: PersonProfileInfo = {
  resourceId: "res-1",
  email: "alice@example.com",
  jobTitleId: "jt-1",
  departmentId: "dept-1",
  linkedUserId: undefined,
  notes: "Test notes",
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-01-01T00:00:00Z",
  jobTitleName: "Software Engineer",
  departmentPath: "Engineering / Frontend",
};

describe("person-profiles-api", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("getPersonProfile", () => {
    it("fetches person profile by resource id", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockPersonProfile),
      });

      const result = await getPersonProfile("res-1");
      expect(result).toEqual(mockPersonProfile);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/person-profiles/res-1",
        expect.any(Object)
      );
    });

    it("returns null when profile not found", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 404,
        json: () => Promise.resolve(null),
      });

      const result = await getPersonProfile("not-found");
      expect(result).toBeNull();
    });
  });

  describe("getPersonJobTitles (bulk)", () => {
    it("returns [] without calling fetch for an empty id list", async () => {
      const result = await getPersonJobTitles([]);
      expect(result).toEqual([]);
      expect(mockFetch).not.toHaveBeenCalled();
    });

    it("sends one POST to /job-titles with the ids in the JSON body (no URL-length limit)", async () => {
      mockFetch.mockResolvedValueOnce({ ok: true, json: () => Promise.resolve([]) });

      await getPersonJobTitles(["a", "b"]);

      expect(mockFetch).toHaveBeenCalledTimes(1);
      const [url, init] = mockFetch.mock.calls[0];
      expect(url).toBe("http://localhost:5000/api/person-profiles/job-titles");
      expect(init).toEqual(
        expect.objectContaining({ method: "POST", credentials: "include" }),
      );
      expect(JSON.parse(init.body as string)).toEqual(["a", "b"]);
    });

    it("returns the parsed job-title label array", async () => {
      const labels = [{ resourceId: "res-1", jobTitleName: "Software Engineer" }];
      mockFetch.mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(labels) });

      const result = await getPersonJobTitles(["res-1"]);
      expect(result).toEqual(labels);
    });
  });

  describe("upsertPersonProfile", () => {
    it("creates a new person profile", async () => {
      const request: UpsertPersonProfileRequest = {
        email: "new@example.com",
        jobTitleId: "jt-1",
        departmentId: "dept-1",
        notes: "New profile",
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockPersonProfile),
      });

      const result = await upsertPersonProfile("res-1", request);
      expect(result).toEqual(mockPersonProfile);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/person-profiles/res-1",
        expect.objectContaining({
          method: "PUT",
          body: JSON.stringify(request),
        })
      );
    });

    it("updates an existing person profile", async () => {
      const request: UpsertPersonProfileRequest = {
        email: "updated@example.com",
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ ...mockPersonProfile, email: request.email }),
      });

      const result = await upsertPersonProfile("res-1", request);
      expect(result.email).toBe(request.email);
    });
  });

  describe("linkUserToPersonProfile", () => {
    it("links a user to a person profile", async () => {
      const request: LinkUserToPersonProfileRequest = {
        userId: "user-123",
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(true),
      });

      const result = await linkUserToPersonProfile("res-1", request);
      expect(result).toBe(true);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/person-profiles/res-1/link",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify(request),
        })
      );
    });
  });

  describe("unlinkUserFromPersonProfile", () => {
    it("sends DELETE to the profile link endpoint", async () => {
      mockFetch.mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(undefined) });

      await unlinkUserFromPersonProfile("res-1");

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/person-profiles/res-1/link",
        expect.objectContaining({ method: "DELETE" }),
      );
    });
  });
});
