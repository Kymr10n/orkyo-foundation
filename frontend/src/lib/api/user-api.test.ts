import { describe, expect, it, vi } from "vitest";
import * as apiClient from "../core/api-client";
import { API_PATHS } from "../core/api-paths";
import {
  cancelInvitation,
  createInvitation,
  deleteUser,
  getInvitations,
  getUsers,
  resendInvitation,
  updateUserRole,
  type UserWithRole,
} from "./user-api";

vi.mock("../core/api-client");

// Headers, credentials and error mapping belong to api-client (api-client.test.ts).

const user: UserWithRole = {
  id: "u1",
  email: "a@example.com",
  displayName: "A",
  role: "editor",
  status: "active",
  createdAt: "2026-01-01T00:00:00Z",
};

describe("user-api", () => {
  it("getUsers unwraps the users array", async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue({ users: [user] });

    expect(await getUsers()).toEqual([user]);
    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.USERS);
  });

  it("getUsers answers an empty list when the body has none", async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue({});

    expect(await getUsers()).toEqual([]);
  });

  it("getInvitations unwraps the invitations array, or answers an empty list", async () => {
    const invitation = { id: "i1", email: "b@example.com", role: "viewer" };
    vi.mocked(apiClient.apiGet).mockResolvedValueOnce({ invitations: [invitation] });
    vi.mocked(apiClient.apiGet).mockResolvedValueOnce({});

    expect(await getInvitations()).toEqual([invitation]);
    expect(await getInvitations()).toEqual([]);
    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.USER_INVITATIONS);
  });

  it("createInvitation posts the invite and returns the answer", async () => {
    const answer = { member: { userId: "u2", email: "c@example.com", role: "editor" }, message: "Added" };
    vi.mocked(apiClient.apiPost).mockResolvedValue(answer);

    expect(await createInvitation({ email: "c@example.com", role: "editor" })).toEqual(answer);
    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.USER_INVITE, {
      email: "c@example.com",
      role: "editor",
    });
  });

  it("resendInvitation posts to the invitation's resend path", async () => {
    vi.mocked(apiClient.apiPost).mockResolvedValue(undefined);

    expect(await resendInvitation("i1")).toBeUndefined();
    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.userInvitationResend("i1"), undefined);
  });

  it("cancelInvitation and deleteUser delete their item", async () => {
    vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

    await cancelInvitation("i1");
    await deleteUser("u1");

    expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.userInvitation("i1"));
    expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.user("u1"));
  });

  it("updateUserRole patches the role and returns the user", async () => {
    vi.mocked(apiClient.apiPatch).mockResolvedValue({ ...user, role: "admin" });

    expect(await updateUserRole("u1", { role: "admin" })).toEqual({ ...user, role: "admin" });
    expect(apiClient.apiPatch).toHaveBeenCalledWith(API_PATHS.userRole("u1"), { role: "admin" });
  });

  it("passes a failure through", async () => {
    vi.mocked(apiClient.apiGet).mockRejectedValue(new Error("Forbidden"));

    await expect(getUsers()).rejects.toThrow("Forbidden");
  });
});
