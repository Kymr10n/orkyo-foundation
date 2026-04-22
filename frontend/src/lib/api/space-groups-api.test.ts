import { beforeEach, describe, expect, it, vi } from "vitest";
import * as apiClient from "../core/api-client";
import {
  createSpaceGroup,
  deleteSpaceGroup,
  getSpaceGroups,
  updateSpaceGroup,
} from "./space-groups-api";

vi.mock("../core/api-client");

const mockSpaceGroup = {
  id: "group-123",
  name: "Floor 1",
  description: "All spaces on floor 1",
  spaces: [],
  createdAt: "2024-01-01T00:00:00Z",
  updatedAt: "2024-01-01T00:00:00Z",
};

describe("spaceGroups", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("getSpaceGroups", () => {
    it("calls apiGet with correct endpoint", async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockSpaceGroup]);

      const result = await getSpaceGroups();

      expect(apiClient.apiGet).toHaveBeenCalledWith("/api/groups");
      expect(result).toEqual([mockSpaceGroup]);
    });

    it("returns empty array on error", async () => {
      vi.mocked(apiClient.apiGet).mockRejectedValue(new Error("Network error"));
      const consoleSpy = vi.spyOn(console, "warn").mockImplementation(() => {});

      const result = await getSpaceGroups();

      expect(result).toEqual([]);
      expect(consoleSpy).toHaveBeenCalledWith(
        "[WARN]",
        "Failed to fetch space groups:",
        expect.any(Error),
      );
      consoleSpy.mockRestore();
    });
  });

  describe("createSpaceGroup", () => {
    it("calls apiPost with correct endpoint and data", async () => {
      const createRequest = {
        name: "Floor 1",
        description: "All spaces on floor 1",
      };
      vi.mocked(apiClient.apiPost).mockResolvedValue(mockSpaceGroup);

      const result = await createSpaceGroup(createRequest);

      expect(apiClient.apiPost).toHaveBeenCalledWith(
        "/api/groups",
        createRequest,
      );
      expect(result).toEqual(mockSpaceGroup);
    });
  });

  describe("updateSpaceGroup", () => {
    it("calls apiPut with correct endpoint and data", async () => {
      const updateRequest = { name: "Updated Floor 1" };
      const updatedGroup = { ...mockSpaceGroup, name: "Updated Floor 1" };
      vi.mocked(apiClient.apiPut).mockResolvedValue(updatedGroup);

      const result = await updateSpaceGroup("group-123", updateRequest);

      expect(apiClient.apiPut).toHaveBeenCalledWith(
        "/api/groups/group-123",
        updateRequest,
      );
      expect(result).toEqual(updatedGroup);
    });
  });

  describe("deleteSpaceGroup", () => {
    it("calls apiDelete with correct endpoint", async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

      await deleteSpaceGroup("group-123");

      expect(apiClient.apiDelete).toHaveBeenCalledWith("/api/groups/group-123");
    });
  });
});
