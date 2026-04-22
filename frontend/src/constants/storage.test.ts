import { describe, it, expect } from "vitest";
import { STORAGE_KEYS } from "./storage";

describe("STORAGE_KEYS", () => {
  it("has 5 keys", () => {
    expect(Object.keys(STORAGE_KEYS)).toHaveLength(5);
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
    expect(STORAGE_KEYS.ACTIVE_MEMBERSHIP).toBe("active_membership");
    expect(STORAGE_KEYS.TENANT_SLUG).toBe("tenant_slug");
    expect(STORAGE_KEYS.THEME).toBe("theme");
    expect(STORAGE_KEYS.SELECTED_SITE_ID).toBe("selectedSiteId");
    expect(STORAGE_KEYS.SIDEBAR_COLLAPSED).toBe("sidebar-collapsed");
  });
});
