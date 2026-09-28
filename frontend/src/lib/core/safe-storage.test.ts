import { afterEach, describe, expect, it, vi } from "vitest";
import { safeStorage } from "./safe-storage";
import { logger } from "./logger";

describe("safeStorage", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it("reads, writes and removes a value", () => {
    safeStorage.set("k", "v");
    expect(safeStorage.get("k")).toBe("v");
    safeStorage.remove("k");
    expect(safeStorage.get("k")).toBeNull();
  });

  it("answers null and logs when storage throws, instead of throwing", () => {
    const error = vi.spyOn(logger, "error").mockImplementation(() => {});
    const blocked = () => {
      throw new DOMException("blocked", "SecurityError");
    };
    vi.spyOn(localStorage, "getItem").mockImplementation(blocked);
    vi.spyOn(localStorage, "setItem").mockImplementation(blocked);
    vi.spyOn(localStorage, "removeItem").mockImplementation(blocked);

    expect(safeStorage.get("k")).toBeNull();
    expect(() => safeStorage.set("k", "v")).not.toThrow();
    expect(() => safeStorage.remove("k")).not.toThrow();
    expect(error).toHaveBeenCalledTimes(3);
  });
});
