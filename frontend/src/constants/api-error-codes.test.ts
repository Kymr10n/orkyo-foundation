import { describe, it, expect } from "vitest";
import { API_ERROR_CODES } from "./api-error-codes";
import type { ApiErrorBody } from "./api-error-codes";

describe("API_ERROR_CODES", () => {
  it("has 6 error codes", () => {
    expect(Object.keys(API_ERROR_CODES)).toHaveLength(6);
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
    expect(API_ERROR_CODES.ACCOUNT_LOCKED).toBe("account_locked");
  });
});

describe("ApiErrorBody", () => {
  // Mirrors the backend's OrkyoProblemDetails: RFC 7807 plus the extensions the frontend
  // switches on. The pre-#96 `{error, message}` fields are gone, not aliased.
  it("accepts a full problem body", () => {
    const body: ApiErrorBody = {
      type: "https://orkyo.app/problems/session_expired",
      title: "Unauthorized",
      detail: "Your session has expired",
      status: 401,
      code: API_ERROR_CODES.SESSION_EXPIRED,
      returnTo: "/site-admin",
    };
    expect(body.code).toBe("session_expired");
    expect(body.detail).toBe("Your session has expired");
    expect(body.returnTo).toBe("/site-admin");
  });

  it("accepts a validation problem carrying per-field errors", () => {
    const body: ApiErrorBody = {
      title: "Bad Request",
      detail: "One or more fields failed validation.",
      status: 400,
      code: "validation_error",
      errors: { Name: ["Name must not be empty."] },
    };
    expect(body.errors?.Name).toEqual(["Name must not be empty."]);
  });

  it("accepts a minimal error body", () => {
    const body: ApiErrorBody = {};
    expect(body.detail).toBeUndefined();
  });
});
