import { describe, it, expect } from "vitest";
import { TENANT_HEADER_NAME, CORRELATION_ID_HEADER_NAME, COOKIE_NAMES } from "./http";

describe("HTTP constants", () => {
  it("TENANT_HEADER_NAME is defined", () => {
    expect(typeof TENANT_HEADER_NAME).toBe("string");
    expect(TENANT_HEADER_NAME.length).toBeGreaterThan(0);
  });

  it("CORRELATION_ID_HEADER_NAME is defined", () => {
    expect(typeof CORRELATION_ID_HEADER_NAME).toBe("string");
    expect(CORRELATION_ID_HEADER_NAME.length).toBeGreaterThan(0);
  });
});

describe("COOKIE_NAMES", () => {
  it("THEME cookie is defined", () => {
    expect(COOKIE_NAMES.THEME).toBe("orkyo-theme");
  });
});
