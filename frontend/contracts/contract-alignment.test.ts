/**
 * Contract Alignment Tests
 *
 * These tests ensure that frontend contract constants stay in sync
 * with backend constants. If these tests fail, it means the API contract
 * has changed and both sides need to be updated together.
 *
 * CRITICAL: Do not modify expected values without coordinating with backend team.
 */

import {
  ApiHeaders,
  AuthErrorCodes,
  Claims,
  ErrorCodes,
  Roles,
} from "@/contracts";
import { describe, expect, it } from "vitest";

describe("Contract Alignment - API Headers", () => {
  it("should match backend HeaderConstants.cs", () => {
    // These values MUST match backend/api/Constants/HeaderConstants.cs
    expect(ApiHeaders.TenantSlug).toBe("X-Tenant-Slug");
    expect(ApiHeaders.CorrelationId).toBe("X-Correlation-ID");
  });
});

describe("Contract Alignment - Claims", () => {
  it("should match backend ClaimConstants.cs", () => {
    // These values MUST match backend/api/Constants/ClaimConstants.cs
    expect(Claims.UserId).toBe("user_id");
    expect(Claims.TenantSlug).toBe("tenant_slug");
    expect(Claims.TenantId).toBe("tenant_id");
    expect(Claims.IsTenantAdmin).toBe("is_tenant_admin");
    expect(Claims.Subject).toBe("sub");
    expect(Claims.Email).toBe("email");
    expect(Claims.PreferredUsername).toBe("preferred_username");
  });
});

describe("Contract Alignment - Roles", () => {
  it("should match backend RoleConstants.cs", () => {
    // These values MUST match backend/api/Constants/RoleConstants.cs
    expect(Roles.Admin).toBe("admin");
    expect(Roles.Editor).toBe("editor");
    expect(Roles.Viewer).toBe("viewer");
    expect(Roles.None).toBe("none");
  });
});

describe("Contract Alignment - Error Codes", () => {
  it("should match backend ErrorCodes.cs", () => {
    // These values MUST match backend/api/Constants/ErrorCodes.cs
    expect(ErrorCodes.NotFound).toBe("NOT_FOUND");
    expect(ErrorCodes.ValidationError).toBe("VALIDATION_ERROR");
    expect(ErrorCodes.Conflict).toBe("CONFLICT");
  });

  it("should match backend ProblemDetailsHelper.AuthCodes", () => {
    // These values MUST match backend/api/Helpers/ProblemDetailsHelper.cs AuthCodes
    expect(AuthErrorCodes.IdentityNotLinked).toBe("identity_not_linked");
    expect(AuthErrorCodes.NotInvited).toBe("not_invited");
    expect(AuthErrorCodes.EmailNotVerified).toBe("email_not_verified");
    expect(AuthErrorCodes.AccountInactive).toBe("account_inactive");
    expect(AuthErrorCodes.InvalidToken).toBe("invalid_token");
  });
});
