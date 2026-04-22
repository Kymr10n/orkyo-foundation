import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  deleteFloorplan,
  fetchFloorplanViewData,
  fetchFloorplanImageUrl,
  getFloorplanMetadata,
  uploadFloorplan,
} from "./floorplan-api";

// Minimal XHR mock — records calls and exposes trigger helpers
function makeMockXhr() {
  const listeners: Record<string, (() => void)[]> = {};
  const uploadListeners: Record<string, ((e: ProgressEvent) => void)[]> = {};
  const mock = {
    status: 200,
    responseText: "",
    upload: {
      addEventListener: vi.fn((event: string, cb: (e: ProgressEvent) => void) => {
        (uploadListeners[event] ??= []).push(cb);
      }),
    },
    addEventListener: vi.fn((event: string, cb: () => void) => {
      (listeners[event] ??= []).push(cb);
    }),
    open: vi.fn(),
    setRequestHeader: vi.fn(),
    send: vi.fn(),
    // helpers to trigger events in tests
    _trigger: (event: string) => listeners[event]?.forEach((cb) => cb()),
    _triggerUpload: (event: string, e: ProgressEvent) =>
      uploadListeners[event]?.forEach((cb) => cb(e)),
  };
  return mock;
}

// Mock api-utils
vi.mock("../core/api-utils", () => ({
  API_BASE_URL: "http://localhost:5000",
  getApiHeaders: vi.fn(() => ({
    "Content-Type": "application/json",
    "X-Tenant-Slug": "test-tenant",
  })),
}));

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
}));

describe("floorplan-api", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("uploadFloorplan", () => {
    it("resolves with metadata on successful upload", async () => {
      const mockMetadata = {
        imagePath: "/storage/floorplans/test.png",
        mimeType: "image/png",
        fileSizeBytes: 5000,
        widthPx: 1024,
        heightPx: 768,
        uploadedAt: "2024-01-01T00:00:00Z",
      };
      const xhr = makeMockXhr();
      xhr.status = 200;
      xhr.responseText = JSON.stringify({ success: true, metadata: mockMetadata });
      vi.stubGlobal("XMLHttpRequest", vi.fn(function () { return xhr; }));

      const file = new File(["content"], "floor.png", { type: "image/png" });
      const promise = uploadFloorplan("site-123", file);
      xhr._trigger("load");

      await expect(promise).resolves.toEqual(mockMetadata);
      expect(xhr.open).toHaveBeenCalledWith(
        "POST",
        "http://localhost:5000/api/sites/site-123/floorplan",
      );
      expect(xhr.send).toHaveBeenCalled();
    });

    it("rejects with server error message on non-2xx response", async () => {
      const xhr = makeMockXhr();
      xhr.status = 400;
      xhr.responseText = JSON.stringify({ message: "File too large" });
      vi.stubGlobal("XMLHttpRequest", vi.fn(function () { return xhr; }));

      const file = new File(["x"], "floor.png", { type: "image/png" });
      const promise = uploadFloorplan("site-123", file);
      xhr._trigger("load");

      await expect(promise).rejects.toThrow("File too large");
    });

    it("rejects with generic message when error body is not JSON", async () => {
      const xhr = makeMockXhr();
      xhr.status = 500;
      xhr.responseText = "Internal Server Error";
      vi.stubGlobal("XMLHttpRequest", vi.fn(function () { return xhr; }));

      const file = new File(["x"], "floor.png", { type: "image/png" });
      const promise = uploadFloorplan("site-123", file);
      xhr._trigger("load");

      await expect(promise).rejects.toThrow("Upload failed with status 500");
    });

    it("rejects on network error", async () => {
      const xhr = makeMockXhr();
      vi.stubGlobal("XMLHttpRequest", vi.fn(function () { return xhr; }));

      const file = new File(["x"], "floor.png", { type: "image/png" });
      const promise = uploadFloorplan("site-123", file);
      xhr._trigger("error");

      await expect(promise).rejects.toThrow("Network error during upload");
    });

    it("rejects when upload is aborted", async () => {
      const xhr = makeMockXhr();
      vi.stubGlobal("XMLHttpRequest", vi.fn(function () { return xhr; }));

      const file = new File(["x"], "floor.png", { type: "image/png" });
      const promise = uploadFloorplan("site-123", file);
      xhr._trigger("abort");

      await expect(promise).rejects.toThrow("Upload cancelled");
    });

    it("calls onProgress with percentage when provided", async () => {
      const xhr = makeMockXhr();
      xhr.status = 200;
      xhr.responseText = JSON.stringify({
        success: true,
        metadata: { imagePath: "/f.png", mimeType: "image/png", fileSizeBytes: 1, widthPx: 1, heightPx: 1, uploadedAt: "" },
      });
      vi.stubGlobal("XMLHttpRequest", vi.fn(function () { return xhr; }));

      const onProgress = vi.fn();
      const file = new File(["x"], "floor.png", { type: "image/png" });
      const promise = uploadFloorplan("site-123", file, onProgress);

      xhr._triggerUpload("progress", { lengthComputable: true, loaded: 50, total: 100 } as ProgressEvent);
      xhr._trigger("load");

      await promise;
      expect(onProgress).toHaveBeenCalledWith(50);
    });

    it("does not set Content-Type header (lets browser set multipart boundary)", async () => {
      const xhr = makeMockXhr();
      xhr.status = 200;
      xhr.responseText = JSON.stringify({
        success: true,
        metadata: { imagePath: "/f.png", mimeType: "image/png", fileSizeBytes: 1, widthPx: 1, heightPx: 1, uploadedAt: "" },
      });
      vi.stubGlobal("XMLHttpRequest", vi.fn(function () { return xhr; }));

      const file = new File(["x"], "floor.png", { type: "image/png" });
      const promise = uploadFloorplan("site-123", file);
      xhr._trigger("load");
      await promise;

      const headerCalls = xhr.setRequestHeader.mock.calls as [string, string][];
      const contentTypeSet = headerCalls.some(
        ([key]) => key.toLowerCase() === "content-type",
      );
      expect(contentTypeSet).toBe(false);
    });
  });

  describe("fetchFloorplanImageUrl", () => {
    it("fetches image via apiRawFetch and returns a data URL", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      const mockBlob = new Blob(["fake-image"], { type: "image/png" });
      (apiRawFetch as ReturnType<typeof vi.fn>).mockResolvedValue({
        blob: () => Promise.resolve(mockBlob),
      });

      const result = await fetchFloorplanImageUrl("site-123");

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

      await expect(fetchFloorplanImageUrl("site-123")).rejects.toThrow(
        "Failed to fetch floorplan: Unauthorized",
      );
    });

    it("does not embed tenant in the URL", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      const mockBlob = new Blob(["img"], { type: "image/png" });
      (apiRawFetch as ReturnType<typeof vi.fn>).mockResolvedValue({
        blob: () => Promise.resolve(mockBlob),
      });

      await fetchFloorplanImageUrl("site-456");

      const calledPath = (apiRawFetch as ReturnType<typeof vi.fn>).mock.calls[0][0] as string;
      expect(calledPath).not.toContain("tenant");
    });
  });

  describe("getFloorplanMetadata", () => {
    it("fetches metadata via apiRawFetch and returns it", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      const mockMetadata = {
        imagePath: "/storage/floorplans/test.png",
        mimeType: "image/png",
        fileSizeBytes: 12345,
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

  describe("fetchFloorplanViewData", () => {
    it("returns image URL and dimensions when metadata exists", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      const mockBlob = new Blob(["img"], { type: "image/png" });

      (apiRawFetch as ReturnType<typeof vi.fn>)
        .mockResolvedValueOnce({ json: () => Promise.resolve({ widthPx: 1200, heightPx: 800 }) })
        .mockResolvedValueOnce({ blob: () => Promise.resolve(mockBlob) });

      const result = await fetchFloorplanViewData("site-123");

      expect(result).not.toBeNull();
      expect(result!.blobUrl).toMatch(/^data:/);
      expect(result!.widthPx).toBe(1200);
      expect(result!.heightPx).toBe(800);
    });

    it("returns null when metadata is null and does not request the image", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      const mock = apiRawFetch as ReturnType<typeof vi.fn>;
      mock.mockResolvedValueOnce({ json: () => Promise.resolve(null) });

      const result = await fetchFloorplanViewData("site-empty");

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

      await expect(fetchFloorplanViewData("site-empty")).resolves.toBeNull();
    });

    it("propagates errors when metadata exists but the image fetch fails", async () => {
      const { apiRawFetch } = await import("../core/api-client");
      (apiRawFetch as ReturnType<typeof vi.fn>)
        .mockResolvedValueOnce({
          json: () => Promise.resolve({ widthPx: 1200, heightPx: 800 }),
        })
        .mockRejectedValueOnce(new Error("Network error"));

      await expect(fetchFloorplanViewData("site-broken")).rejects.toThrow("Network error");
    });
  });
});
