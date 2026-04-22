import { describe, it, expect } from "vitest";
import { API_ERROR_CODES } from "./api-error-codes";
import type { ApiErrorBody } from "./api-error-codes";

describe("API_ERROR_CODES", () => {
  it("has 4 error codes", () => {
    expect(Object.keys(API_ERROR_CODES)).toHaveLength(4);
  });

  it("values are all lowercase snake_case", () => {
    for (const value of Object.values(API_ERROR_CODES)) {
      expect(value).toMatch(/^[a-z]+(_[a-z]+)*$/);
    }
  });

  it("has expected values", () => {
    expect(API_ERROR_CODES.SESSION_EXPIRED).toBe("session_expired");
    expect(API_ERROR_CODES.BREAK_GLASS_EXPIRED).toBe("break_glass_expired");
    expect(API_ERROR_CODES.BREAK_GLASS_HARD_CAP_REACHED).toBe("break_glass_hard_cap_reached");
    expect(API_ERROR_CODES.FORBIDDEN).toBe("forbidden");
  });
});

describe("ApiErrorBody", () => {
  it("accepts a valid error body shape", () => {
    const body: ApiErrorBody = {
      error: "session_expired",
      message: "Your session has expired",
      code: API_ERROR_CODES.SESSION_EXPIRED,
      returnTo: "/admin",
    };
    expect(body.code).toBe("session_expired");
    expect(body.returnTo).toBe("/admin");
  });

  it("accepts a minimal error body", () => {
    const body: ApiErrorBody = {};
    expect(body.error).toBeUndefined();
  });
});
