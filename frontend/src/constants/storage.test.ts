import { describe, it, expect } from "vitest";
import { STORAGE_KEYS } from "./storage";

describe("STORAGE_KEYS", () => {
  it("has 6 keys", () => {
    expect(Object.keys(STORAGE_KEYS)).toHaveLength(6);
  });

  it("values are all non-empty strings", () => {
    for (const value of Object.values(STORAGE_KEYS)) {
      expect(typeof value).toBe("string");
      expect(value.length).toBeGreaterThan(0);
    }
  });

  it("values are all unique", () => {
    const values = Object.values(STORAGE_KEYS);
    expect(new Set(values).size).toBe(values.length);
  });

  it("has expected keys", () => {
    expect(STORAGE_KEYS.TENANT_SLUG).toBe("tenant_slug");
    expect(STORAGE_KEYS.LAYOUT).toBe("orkyo.layout");
    expect(STORAGE_KEYS.SELECTED_SITE_ID).toBe("orkyo.site");
    expect(STORAGE_KEYS.REQUEST_TREE).toBe("orkyo.requestTree");
    expect(STORAGE_KEYS.ASSISTANT_WIDTH).toBe("orkyo.assistant.width");
    expect(STORAGE_KEYS.TYPE_FILTER_PREFIX).toBe("orkyo.typeFilter.");
  });
});
