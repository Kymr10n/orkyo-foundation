import { describe, expect, it, vi } from "vitest";
import * as apiClient from "../core/api-client";
import { API_PATHS } from "../core/api-paths";
import { getConflicts } from "./conflicts-api";

vi.mock("../core/api-client");

describe("conflicts-api — getConflicts", () => {
  it("requests the all-time registry (no query params) when no window is given", async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue([]);

    await getConflicts();

    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.CONFLICTS, undefined);
  });

  it("passes from/to ISO query params when a window is given", async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue([]);
    const from = new Date("2026-05-01T00:00:00Z");
    const to = new Date("2026-05-08T00:00:00Z");

    await getConflicts({ from, to });

    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.CONFLICTS, {
      params: { from: from.toISOString(), to: to.toISOString() },
    });
  });

  it("returns the parsed registry array", async () => {
    const registry = [{ requestId: "r1", conflicts: [] }];
    vi.mocked(apiClient.apiGet).mockResolvedValue(registry);

    expect(await getConflicts()).toEqual(registry);
  });
});
