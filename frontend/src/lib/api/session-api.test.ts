import { describe, expect, it, vi } from "vitest";
import * as apiClient from "../core/api-client";
import { API_PATHS } from "../core/api-paths";
import { acceptTos, markTourSeen } from "./session-api";
import { TENANT_HEADER_NAME } from "@foundation/src/constants/http";

vi.mock("../core/api-client");

// Headers, credentials and error mapping belong to apiPost (api-client.test.ts).
describe("session-api", () => {
  it("acceptTos posts the accepted version with no body to parse", async () => {
    vi.mocked(apiClient.apiPost).mockResolvedValue(undefined);

    await acceptTos("2026-02");

    expect(apiClient.apiPost).toHaveBeenCalledWith(
      API_PATHS.SESSION.TOS_ACCEPT,
      { tosVersion: "2026-02" },
      expect.objectContaining({ skipJsonParse: true, omitHeaders: [TENANT_HEADER_NAME] }),
    );
  });

  it("markTourSeen posts to the tour endpoint and resolves to void", async () => {
    vi.mocked(apiClient.apiPost).mockResolvedValue(undefined);

    expect(await markTourSeen()).toBeUndefined();

    expect(apiClient.apiPost).toHaveBeenCalledWith(
      API_PATHS.SESSION.TOUR_SEEN,
      {},
      expect.objectContaining({ skipJsonParse: true, omitHeaders: [TENANT_HEADER_NAME] }),
    );
  });

  it("passes a failure through", async () => {
    vi.mocked(apiClient.apiPost).mockRejectedValue(new Error("Not authenticated"));

    await expect(acceptTos("2026-02")).rejects.toThrow("Not authenticated");
  });
});
