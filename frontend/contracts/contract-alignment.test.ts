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
  FeatureKeys,
  PlanCodes,
  Roles,
} from "./index";
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

describe("Contract Alignment - Plan Codes", () => {
  it("should match orkyo-saas TierCodes.cs and SinglePlanInfoProvider.PlanCode", () => {
    // These values MUST match orkyo-saas backend/src/Models/TierCodes.cs (subscription_tiers
    // .code) and foundation SinglePlanInfoProvider.PlanCode. The wire carries these codes —
    // never subscription_tiers.display_name, which reads as "not entitled" on every compare.
    expect(PlanCodes.Free).toBe("free");
    expect(PlanCodes.Professional).toBe("professional");
    expect(PlanCodes.Enterprise).toBe("enterprise");
    expect(PlanCodes.Community).toBe("community");
  });

  it("should match backend FeatureKeys (IFeatureGate.cs)", () => {
    // These values MUST match backend/core/Security/Features/IFeatureGate.cs — and the set
    // must match FeatureKeys.Enforced, which is what the session payload reports.
    expect(FeatureKeys.ApiAccess).toBe("api_access_enabled");
    expect(FeatureKeys.AuditLog).toBe("audit_log_enabled");
    expect(FeatureKeys.DataExport).toBe("data_export_enabled");
    expect(FeatureKeys.CalendarFeed).toBe("calendar_feed_enabled");
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
