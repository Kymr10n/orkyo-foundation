import { describe, expect, it, vi } from "vitest";
import * as apiClient from "../core/api-client";
import { API_PATHS } from "../core/api-paths";
import { ApiError } from "../core/api-utils";
import {
  addAdminTenantMember,
  auditBreakGlassEntry,
  auditBreakGlassExit,
  createAdminTenant,
  deactivateAdminUser,
  deleteAdminQuotaOverride,
  deleteAdminTenant,
  deleteAdminUser,
  getAdminDiagnostics,
  getAdminSettings,
  getAdminTenantQuotas,
  getAdminTenantsUsage,
  getAdminTenantMembers,
  getAdminTenants,
  getAdminUser,
  getAdminUsers,
  getBreakGlassSessionStatus,
  getPlatformAuditEvents,
  promoteSiteAdmin,
  reactivateAdminUser,
  removeAdminTenantMember,
  renewBreakGlassSession,
  revokeSiteAdmin,
  updateAdminSettings,
  updateAdminTenant,
  updateAdminTenantMember,
  upsertAdminQuotaOverride,
} from "./admin-api";

vi.mock("../core/api-client");

// Every function is a thin wrapper: these pin the path, the verb, the body and the params each
// builds. URL building, headers, credentials and error mapping belong to api-client
// (api-client.test.ts); slug encoding to api-paths.

const sentParams = () =>
  (vi.mocked(apiClient.apiGet).mock.calls[0][1] as { params: Record<string, unknown> }).params;

describe("admin-api — list params", () => {
  it("getAdminTenants sends search and paging only when given", async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue({ tenants: [], totalCount: 60 });

    const result = await getAdminTenants("acme", { page: 2, pageSize: 25 });

    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.ADMIN.TENANTS, expect.any(Object));
    expect(sentParams()).toEqual({ search: "acme", page: "2", pageSize: "25" });
    expect(result.totalCount).toBe(60);
  });

  it("getAdminTenants sends no params for the unpaged picker", async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue({ tenants: [] });

    await getAdminTenants();

    expect(sentParams()).toEqual({});
  });

  it("getAdminUsers sends search and status only when given", async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue({ users: [] });

    await getAdminUsers("ann", "active");
    await getAdminUsers();

    expect(apiClient.apiGet).toHaveBeenNthCalledWith(1, API_PATHS.ADMIN.USERS, {
      params: { search: "ann", status: "active" },
    });
    expect(apiClient.apiGet).toHaveBeenNthCalledWith(2, API_PATHS.ADMIN.USERS, { params: {} });
  });

  it("getAdminTenantMembers sends status and paging only when given", async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue({ members: [] });

    await getAdminTenantMembers("t1", "active", { page: 1, pageSize: 50 });

    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.ADMIN.tenantMembers("t1"), {
      params: { status: "active", page: "1", pageSize: "50" },
    });
  });

  it("getPlatformAuditEvents sends only the filters that are set", async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue({ items: [] });

    await getPlatformAuditEvents({ action: "user.deleted", from: "2026-01-01", page: 1, pageSize: 20 });

    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.ADMIN.AUDIT, {
      params: { action: "user.deleted", from: "2026-01-01", page: 1, pageSize: 20 },
    });
  });
});

describe("admin-api — paths and bodies", () => {
  it.each([
    ["createAdminTenant", () => createAdminTenant({ slug: "acme", displayName: "Acme", ownerEmail: "o@a.test" }),
      "apiPost", [API_PATHS.ADMIN.TENANTS, { slug: "acme", displayName: "Acme", ownerEmail: "o@a.test" }]],
    ["updateAdminTenant", () => updateAdminTenant("t1", { status: "suspended" }),
      "apiPatch", [API_PATHS.ADMIN.tenant("t1"), { status: "suspended" }]],
    ["deleteAdminTenant", () => deleteAdminTenant("t1"), "apiDelete", [API_PATHS.ADMIN.tenant("t1")]],
    ["getAdminUser", () => getAdminUser("u1"), "apiGet", [API_PATHS.ADMIN.user("u1")]],
    ["deactivateAdminUser", () => deactivateAdminUser("u1"), "apiPost", [API_PATHS.ADMIN.userDeactivate("u1"), {}]],
    ["reactivateAdminUser", () => reactivateAdminUser("u1"), "apiPost", [API_PATHS.ADMIN.userReactivate("u1"), {}]],
    ["deleteAdminUser", () => deleteAdminUser("u1"), "apiDelete", [API_PATHS.ADMIN.user("u1")]],
    ["promoteSiteAdmin", () => promoteSiteAdmin("u1"), "apiPost", [API_PATHS.ADMIN.userPromoteSiteAdmin("u1"), {}]],
    ["revokeSiteAdmin", () => revokeSiteAdmin("u1"), "apiPost", [API_PATHS.ADMIN.userRevokeSiteAdmin("u1"), {}]],
    ["addAdminTenantMember", () => addAdminTenantMember("t1", { userId: "u1", role: "editor" }),
      "apiPost", [API_PATHS.ADMIN.tenantMembers("t1"), { userId: "u1", role: "editor" }]],
    ["updateAdminTenantMember", () => updateAdminTenantMember("t1", "u1", { role: "viewer" }),
      "apiPatch", [API_PATHS.ADMIN.tenantMember("t1", "u1"), { role: "viewer" }]],
    ["removeAdminTenantMember", () => removeAdminTenantMember("t1", "u1"),
      "apiDelete", [API_PATHS.ADMIN.tenantMember("t1", "u1")]],
    ["auditBreakGlassEntry", () => auditBreakGlassEntry("acme", "incident 42"),
      "apiPost", [API_PATHS.ADMIN.BREAK_GLASS_ENTRY, { tenantSlug: "acme", reason: "incident 42" }]],
    ["auditBreakGlassExit", () => auditBreakGlassExit("s1"),
      "apiPost", [API_PATHS.ADMIN.BREAK_GLASS_EXIT, { sessionId: "s1" }]],
    ["renewBreakGlassSession", () => renewBreakGlassSession("s1"),
      "apiPost", [API_PATHS.ADMIN.BREAK_GLASS_RENEW, { sessionId: "s1" }]],
    ["getAdminSettings", () => getAdminSettings(), "apiGet", [API_PATHS.ADMIN.SETTINGS]],
    ["getAdminDiagnostics", () => getAdminDiagnostics(), "apiGet", [API_PATHS.ADMIN.DIAGNOSTICS]],
    ["getAdminTenantsUsage", () => getAdminTenantsUsage(), "apiGet", [API_PATHS.ADMIN.TENANTS_USAGE]],
    ["getAdminTenantQuotas", () => getAdminTenantQuotas("t1"), "apiGet", [API_PATHS.ADMIN.tenantQuotas("t1")]],
    ["updateAdminSettings", () => updateAdminSettings({ "a.b": "1" }),
      "apiPut", [API_PATHS.ADMIN.SETTINGS, { settings: { "a.b": "1" } }]],
    ["upsertAdminQuotaOverride", () => upsertAdminQuotaOverride("t1", "sites", { limitValue: 5 }),
      "apiPut", [API_PATHS.ADMIN.tenantQuotaOverride("t1", "sites"), { limitValue: 5 }]],
    ["deleteAdminQuotaOverride", () => deleteAdminQuotaOverride("t1", "sites"),
      "apiDelete", [API_PATHS.ADMIN.tenantQuotaOverride("t1", "sites")]],
  ] as const)("%s", async (_name, call, verb, args) => {
    vi.mocked(apiClient[verb]).mockResolvedValue({} as never);

    await call();

    expect(apiClient[verb]).toHaveBeenCalledWith(...args);
  });
});

describe("admin-api — getBreakGlassSessionStatus", () => {
  const session = {
    sessionId: "abc",
    tenantSlug: "acme",
    createdAt: "2026-04-18T12:00:00Z",
    expiresAt: "2026-04-18T13:00:00Z",
    absoluteExpiresAt: "2026-04-18T20:00:00Z",
  };

  it("returns the session for the tenant", async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue(session);

    expect(await getBreakGlassSessionStatus("acme")).toEqual(session);
    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.ADMIN.breakGlassSession("acme"));
  });

  it("returns null when there is no active session (404)", async () => {
    vi.mocked(apiClient.apiGet).mockRejectedValue(new ApiError("No session", 404, "break_glass_expired"));

    expect(await getBreakGlassSessionStatus("acme")).toBeNull();
  });

  it("rethrows a server error instead of reporting no session", async () => {
    vi.mocked(apiClient.apiGet).mockRejectedValue(new ApiError("Boom", 500));

    await expect(getBreakGlassSessionStatus("acme")).rejects.toMatchObject({ status: 500 });
  });
});
