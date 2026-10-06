import { describe, expect, it, vi } from "vitest";
import * as apiClient from "../core/api-client";
import { API_PATHS } from "../core/api-paths";
import { TENANT_HEADER_NAME } from "@foundation/src/constants/http";
import {
  canCreateTenant,
  cancelTenantDeletion,
  createTenant,
  deleteOwnAccount,
  deleteTenant,
  exportPersonalData,
  getStarterTemplates,
  getTenantMemberships,
  leaveTenant,
} from "./tenant-account-api";

vi.mock("../core/api-client");

// These run before a tenant is chosen, so every call drops the tenant header. Credentials,
// CSRF and error mapping belong to api-client (api-client.test.ts).
const noTenant = expect.objectContaining({ omitHeaders: [TENANT_HEADER_NAME] });

describe("tenant-account-api", () => {
  it("exportPersonalData reads the person's export without a tenant", async () => {
    const doc = { schemaVersion: "1.0" };
    vi.mocked(apiClient.apiGet).mockResolvedValue(doc);

    expect(await exportPersonalData()).toEqual(doc);
    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.ACCOUNT.EXPORT, noTenant);
  });

  it("deleteOwnAccount posts the typed email and expects an empty body", async () => {
    vi.mocked(apiClient.apiPost).mockResolvedValue(undefined);

    await deleteOwnAccount("alex@example.com");
    expect(apiClient.apiPost).toHaveBeenCalledWith(
      API_PATHS.ACCOUNT.DELETE,
      { confirmEmail: "alex@example.com" },
      expect.objectContaining({ omitHeaders: [TENANT_HEADER_NAME], skipJsonParse: true }),
    );
  });

  it("canCreateTenant reads the quota answer", async () => {
    const answer = { canCreate: true, currentCount: 1, maxAllowed: 5 };
    vi.mocked(apiClient.apiGet).mockResolvedValue(answer);

    expect(await canCreateTenant()).toEqual(answer);
    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.TENANTS.CAN_CREATE, noTenant);
  });

  it("createTenant posts the request and returns the created tenant", async () => {
    const request = { slug: "acme", displayName: "Acme", starterTemplate: "empty" };
    const created = { id: "t1", slug: "acme", displayName: "Acme", state: "active" };
    vi.mocked(apiClient.apiPost).mockResolvedValue(created);

    expect(await createTenant(request)).toEqual(created);
    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.TENANTS.CREATE, request, noTenant);
  });

  it("getTenantMemberships and getStarterTemplates read their lists", async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue([]);

    await getTenantMemberships();
    await getStarterTemplates();

    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.TENANTS.MEMBERSHIPS, noTenant);
    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.TENANTS.STARTER_TEMPLATES, noTenant);
  });

  it("leaveTenant and cancelTenantDeletion post with no body to parse", async () => {
    vi.mocked(apiClient.apiPost).mockResolvedValue(undefined);

    await leaveTenant("t1");
    await cancelTenantDeletion("t1");

    const noBody = expect.objectContaining({ omitHeaders: [TENANT_HEADER_NAME], skipJsonParse: true });
    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.TENANTS.leave("t1"), {}, noBody);
    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.TENANTS.cancelDeletion("t1"), {}, noBody);
  });

  it("deleteTenant deletes the tenant", async () => {
    vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

    await deleteTenant("t1");

    expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.TENANTS.delete("t1"), noTenant);
  });

  it("passes a failure through", async () => {
    vi.mocked(apiClient.apiGet).mockRejectedValue(new Error("Unauthorized"));

    await expect(canCreateTenant()).rejects.toThrow("Unauthorized");
  });
});
