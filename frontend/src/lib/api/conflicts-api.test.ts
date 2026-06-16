import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as ApiUtils from "../core/api-utils";
import { getConflicts } from "./conflicts-api";

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
      throw new Error(`Error ${response.status}`);
    }),
    API_BASE_URL: "http://localhost:5000",
  };
});

const mockFetch = vi.fn();
global.fetch = mockFetch;

describe("conflicts-api — getConflicts", () => {
  beforeEach(() => vi.clearAllMocks());

  it("requests the all-time registry (no query params) when no window is given", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: () => Promise.resolve([]) });

    await getConflicts();

    const [url, init] = mockFetch.mock.calls[0];
    expect(url).toBe("http://localhost:5000/api/conflicts");
    expect(init).toEqual(expect.objectContaining({ method: "GET", credentials: "include" }));
  });

  it("passes from/to ISO query params when a window is given", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: () => Promise.resolve([]) });
    const from = new Date("2026-05-01T00:00:00Z");
    const to = new Date("2026-05-08T00:00:00Z");

    await getConflicts({ from, to });

    const [url] = mockFetch.mock.calls[0];
    const parsed = new URL(url);
    expect(parsed.pathname).toBe("/api/conflicts");
    expect(parsed.searchParams.get("from")).toBe(from.toISOString());
    expect(parsed.searchParams.get("to")).toBe(to.toISOString());
  });

  it("returns the parsed registry array", async () => {
    const registry = [{ requestId: "r1", conflicts: [] }];
    mockFetch.mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(registry) });

    const result = await getConflicts();
    expect(result).toEqual(registry);
  });
});
