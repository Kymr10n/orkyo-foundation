import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  deleteFloorplan,
  getFloorplanViewData,
  getFloorplanImageUrl,
  getFloorplanMetadata,
  uploadFloorplan,
} from "./floorplan-api";

vi.mock("../core/upload", () => ({ uploadWithProgress: vi.fn() }));

// Mock api-paths
vi.mock("../core/api-paths", () => ({
  API_PATHS: {
    siteFloorplan: (siteId: string) => `/api/sites/${siteId}/floorplan`,
    siteFloorplanMetadata: (siteId: string) =>
      `/api/sites/${siteId}/floorplan/metadata`,
  },
}));

// Mock api-client
vi.mock("../core/api-client", () => ({
  apiDelete: vi.fn(() => Promise.resolve()),
  apiRawFetch: vi.fn(),
  buildUrl: (path: string) => `http://localhost:5000${path}`,
}));

describe("floorplan-api", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("uploadFloorplan", () => {
    it("sends the file as a form and resolves with the metadata", async () => {
      const { uploadWithProgress } = await import("../core/upload");
      const metadata = { assetId: "asset-1", fileName: "test.png" };
      vi.mocked(uploadWithProgress).mockResolvedValue({ success: true, metadata });
      const onProgress = vi.fn();
      const file = new File(["content"], "floor.png", { type: "image/png" });

      await expect(uploadFloorplan("site-123", file, onProgress)).resolves.toEqual(metadata);

      const [url, body, options] = vi.mocked(uploadWithProgress).mock.calls[0];
      expect(url).toBe("http://localhost:5000/api/sites/site-123/floorplan");
      expect((body as FormData).get("file")).toBe(file);
      // No content type: the browser sets it with the multipart boundary.
      expect(options).toEqual({ onProgress });
    });

    it("rejects with the upload's error", async () => {
      const { uploadWithProgress } = await import("../core/upload");
      vi.mocked(uploadWithProgress).mockRejectedValue(new Error("File too large"));

      await expect(uploadFloorplan("site-123", new File(["x"], "floor.png"))).rejects.toThrow("File too large");
    });
  });

  describe("getFloorplanImageUrl", () => {
    it("fetches image via apiRawFetch and returns a data URL", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      const mockBlob = new Blob(["fake-image"], { type: "image/png" });
      (apiRawFetch as ReturnType<typeof vi.fn>).mockResolvedValue({
        blob: () => Promise.resolve(mockBlob),
      });

      const result = await getFloorplanImageUrl("site-123");

      expect(apiRawFetch).toHaveBeenCalledWith(
        "/api/sites/site-123/floorplan",
        "GET",
        { cache: "default" },
      );
      expect(result).toMatch(/^data:image\/png;base64,/);
    });

    it("throws when apiRawFetch rejects", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      (apiRawFetch as ReturnType<typeof vi.fn>).mockRejectedValue(
        new Error("Failed to fetch floorplan: Unauthorized"),
      );

      await expect(getFloorplanImageUrl("site-123")).rejects.toThrow(
        "Failed to fetch floorplan: Unauthorized",
      );
    });

    it("does not embed tenant in the URL", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      const mockBlob = new Blob(["img"], { type: "image/png" });
      (apiRawFetch as ReturnType<typeof vi.fn>).mockResolvedValue({
        blob: () => Promise.resolve(mockBlob),
      });

      await getFloorplanImageUrl("site-456");

      const calledPath = (apiRawFetch as ReturnType<typeof vi.fn>).mock.calls[0][0] as string;
      expect(calledPath).not.toContain("tenant");
    });
  });

  describe("getFloorplanMetadata", () => {
    it("fetches metadata via apiRawFetch and returns it", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      const mockMetadata = {
        assetId: "asset-1",
        fileName: "test.png",
        mimeType: "image/png",
        fileSizeBytes: 12345,
        checksumSha256: "abc",
        widthPx: 800,
        heightPx: 600,
        uploadedAt: "2024-01-01T00:00:00Z",
      };

      (apiRawFetch as ReturnType<typeof vi.fn>).mockResolvedValue({
        json: () => Promise.resolve(mockMetadata),
      });

      const result = await getFloorplanMetadata("site-123");

      expect(apiRawFetch).toHaveBeenCalledWith("/api/sites/site-123/floorplan/metadata");
      expect(result).toEqual(mockMetadata);
    });

    it("returns null when backend returns null", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      (apiRawFetch as ReturnType<typeof vi.fn>).mockResolvedValue({
        json: () => Promise.resolve(null),
      });

      const result = await getFloorplanMetadata("site-no-floorplan");

      expect(result).toBeNull();
    });

    it("throws when apiRawFetch rejects", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      (apiRawFetch as ReturnType<typeof vi.fn>).mockRejectedValue(
        new Error("Not Found"),
      );

      await expect(getFloorplanMetadata("site-404")).rejects.toThrow("Not Found");
    });
  });

  describe("deleteFloorplan", () => {
    it("calls apiDelete with correct path", async () => {
      const { apiDelete } = await import("../core/api-client");

      await deleteFloorplan("site-to-delete");

      expect(apiDelete).toHaveBeenCalledWith(
        "/api/sites/site-to-delete/floorplan",
      );
    });
  });

  describe("getFloorplanViewData", () => {
    it("returns image URL and dimensions when metadata exists", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      const mockBlob = new Blob(["img"], { type: "image/png" });

      (apiRawFetch as ReturnType<typeof vi.fn>)
        .mockResolvedValueOnce({ json: () => Promise.resolve({ widthPx: 1200, heightPx: 800 }) })
        .mockResolvedValueOnce({ blob: () => Promise.resolve(mockBlob) });

      const result = await getFloorplanViewData("site-123");

      expect(result).not.toBeNull();
      expect(result!.blobUrl).toMatch(/^data:/);
      expect(result!.widthPx).toBe(1200);
      expect(result!.heightPx).toBe(800);
    });

    it("returns null when metadata is null and does not request the image", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      const mock = apiRawFetch as ReturnType<typeof vi.fn>;
      mock.mockResolvedValueOnce({ json: () => Promise.resolve(null) });

      const result = await getFloorplanViewData("site-empty");

      expect(result).toBeNull();
      expect(mock).toHaveBeenCalledTimes(1);
      expect(mock).toHaveBeenCalledWith("/api/sites/site-empty/floorplan/metadata");
    });

    // Regression: previously the metadata + image fetches ran in parallel via Promise.all,
    // so a 404 from the image endpoint surfaced as a query error even when the site
    // legitimately had no floorplan. The empty-state UI was unreachable as a result.
    it("returns null without throwing when metadata is null even if image endpoint would 404", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      (apiRawFetch as ReturnType<typeof vi.fn>)
        .mockResolvedValueOnce({ json: () => Promise.resolve(null) })
        .mockRejectedValueOnce(new Error("Not Found"));

      await expect(getFloorplanViewData("site-empty")).resolves.toBeNull();
    });

    it("propagates errors when metadata exists but the image fetch fails", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      (apiRawFetch as ReturnType<typeof vi.fn>)
        .mockResolvedValueOnce({
          json: () => Promise.resolve({ widthPx: 1200, heightPx: 800 }),
        })
        .mockRejectedValueOnce(new Error("Network error"));

      await expect(getFloorplanViewData("site-broken")).rejects.toThrow("Network error");
    });
  });
});
