/**
 * Contract Alignment Tests
 *
 * These tests ensure that frontend contract constants stay in sync
 * with backend constants. If these tests fail, it means the API contract
 * has changed and both sides need to be updated together.
 *
 * CRITICAL: Do not modify expected values without coordinating with backend team.
 */

import { ApiHeaders } from "./apiHeaders";
import { FeatureKeys, PlanCodes } from "./plans";
import { TENANT_ROLE } from "../src/hooks/usePermissions";
import { describe, expect, it } from "vitest";

describe("Contract Alignment - API Headers", () => {
  it("should match backend HeaderConstants.cs", () => {
    // These values MUST match backend/api/Constants/HeaderConstants.cs
    expect(ApiHeaders.TenantSlug).toBe("X-Tenant-Slug");
    expect(ApiHeaders.CorrelationId).toBe("X-Correlation-ID");
  });
});

describe("Contract Alignment - Roles", () => {
  it("should match backend RoleConstants.cs", () => {
    expect(TENANT_ROLE.Admin).toBe("admin");
    expect(TENANT_ROLE.Editor).toBe("editor");
    expect(TENANT_ROLE.Viewer).toBe("viewer");
    expect(TENANT_ROLE.None).toBe("none");
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
