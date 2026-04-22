import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  getAnnouncements,
  createAnnouncement,
  updateAnnouncement,
  deleteAnnouncement,
} from "./announcement-api";
import * as apiClient from "../core/api-client";
import { API_BASE_URL } from "../core/api-utils";

vi.mock("../core/api-client");

const BASE = `${API_BASE_URL}/api/admin/announcements`;

const mockAnnouncement = {
  id: "11111111-1111-1111-1111-111111111111",
  title: "Maintenance",
  body: "Server down on Friday.",
  isImportant: false,
  revision: 1,
  createdAt: "2025-01-15T10:00:00Z",
  createdByEmail: "admin@orkyo.io",
  updatedAt: "2025-01-15T10:00:00Z",
  updatedByEmail: "admin@orkyo.io",
  expiresAt: "2025-04-15T10:00:00Z",
  isExpired: false,
};

describe("announcement-api", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ========================================================================
  // getAnnouncements
  // ========================================================================

  describe("getAnnouncements", () => {
    it("calls apiGet with includeExpired=true by default", async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue({
        announcements: [mockAnnouncement],
      });

      const result = await getAnnouncements();

      expect(apiClient.apiGet).toHaveBeenCalledWith(BASE, {
        params: { includeExpired: "true" },
      });
      expect(result.announcements).toHaveLength(1);
    });

    it("passes includeExpired=false when specified", async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue({ announcements: [] });

      await getAnnouncements(false);

      expect(apiClient.apiGet).toHaveBeenCalledWith(BASE, {
        params: { includeExpired: "false" },
      });
    });
  });

  // ========================================================================
  // createAnnouncement
  // ========================================================================

  describe("createAnnouncement", () => {
    it("calls apiPost with correct URL and payload", async () => {
      vi.mocked(apiClient.apiPost).mockResolvedValue(mockAnnouncement);

      const data = {
        title: "New",
        body: "Body",
        isImportant: true,
        retentionDays: 30,
      };
      const result = await createAnnouncement(data);

      expect(apiClient.apiPost).toHaveBeenCalledWith(BASE, data);
      expect(result.id).toBe(mockAnnouncement.id);
    });
  });

  // ========================================================================
  // updateAnnouncement
  // ========================================================================

  describe("updateAnnouncement", () => {
    it("calls apiPut with correct URL and payload", async () => {
      vi.mocked(apiClient.apiPut).mockResolvedValue(mockAnnouncement);

      const data = {
        title: "Updated",
        body: "Updated body",
        isImportant: false,
      };
      const result = await updateAnnouncement("abc-123", data);

      expect(apiClient.apiPut).toHaveBeenCalledWith(`${BASE}/abc-123`, data);
      expect(result.title).toBe("Maintenance");
    });
  });

  // ========================================================================
  // deleteAnnouncement
  // ========================================================================

  describe("deleteAnnouncement", () => {
    it("calls apiDelete with correct URL", async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

      await deleteAnnouncement("abc-123");

      expect(apiClient.apiDelete).toHaveBeenCalledWith(`${BASE}/abc-123`);
    });
  });
});
