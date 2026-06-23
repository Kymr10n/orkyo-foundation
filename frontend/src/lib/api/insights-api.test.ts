import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  getInsightsOverview,
  getInsightsUtilization,
  getInsightsConflicts,
  getInsightsRequests,
} from "./insights-api";
import { apiGet } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

vi.mock("../core/api-client", () => ({ apiGet: vi.fn(() => Promise.resolve({})) }));

const from = new Date("2026-01-01T00:00:00Z");
const to = new Date("2026-12-31T00:00:00Z");

function lastCall() {
  return vi.mocked(apiGet).mock.calls.at(-1)!;
}

describe("insights-api", () => {
  beforeEach(() => vi.clearAllMocks());

  it("overview sends from/to and omits siteId when not given", async () => {
    await getInsightsOverview(from, to);
    const [path, opts] = lastCall();
    expect(path).toBe(API_PATHS.INSIGHTS.OVERVIEW);
    expect(opts?.params).toEqual({ from: from.toISOString(), to: to.toISOString() });
  });

  it("overview includes siteId when provided", async () => {
    await getInsightsOverview(from, to, "site-1");
    expect(lastCall()[1]?.params).toMatchObject({ siteId: "site-1" });
  });

  it("utilization sends bucket + resourceType", async () => {
    await getInsightsUtilization("person", from, to, "quarter", "site-1");
    const [path, opts] = lastCall();
    expect(path).toBe(API_PATHS.INSIGHTS.UTILIZATION);
    expect(opts?.params).toMatchObject({ bucket: "quarter", resourceType: "person", siteId: "site-1" });
  });

  it("conflicts sends bucket and no resourceType", async () => {
    await getInsightsConflicts(from, to, "month");
    const [path, opts] = lastCall();
    expect(path).toBe(API_PATHS.INSIGHTS.CONFLICTS);
    expect(opts?.params).toMatchObject({ bucket: "month" });
    expect(opts?.params).not.toHaveProperty("resourceType");
  });

  it("requests sends bucket", async () => {
    await getInsightsRequests(from, to, "week", "site-2");
    const [path, opts] = lastCall();
    expect(path).toBe(API_PATHS.INSIGHTS.REQUESTS);
    expect(opts?.params).toMatchObject({ bucket: "week", siteId: "site-2" });
  });
});
