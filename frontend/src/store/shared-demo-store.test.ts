import { beforeEach, describe, expect, it, vi } from "vitest";
import { STORAGE_KEYS } from "@foundation/src/constants/storage";
import { useSharedDemoStore } from "@foundation/src/store/shared-demo-store";

describe("useSharedDemoStore", () => {
  beforeEach(() => {
    sessionStorage.clear();
    useSharedDemoStore.setState({ noticeHidden: false });
  });

  // Restore each spy inside its test: `vi.restoreAllMocks()` does not take a spy off jsdom's
  // sessionStorage proxy, so one left behind would fail every later test's storage call.

  it("persists the choice in sessionStorage", () => {
    useSharedDemoStore.getState().setNoticeHidden(true);
    expect(sessionStorage.getItem(STORAGE_KEYS.SHARED_DEMO_NOTICE)).toContain('"noticeHidden":true');
  });

  it("keeps working when storage reads succeed but writes throw", () => {
    // The session-end paths reset this store right before they navigate away. A quota or
    // private-mode failure on the write must not throw past them, or the visitor is stranded.
    // Spy the instance, not Storage.prototype: jsdom's sessionStorage does not dispatch
    // through the prototype (see session-end.test.ts).
    const setItem = vi.spyOn(window.sessionStorage, "setItem").mockImplementation(() => {
      throw new DOMException("quota", "QuotaExceededError");
    });

    expect(() => useSharedDemoStore.getState().setNoticeHidden(true)).not.toThrow();
    expect(setItem).toHaveBeenCalledWith(STORAGE_KEYS.SHARED_DEMO_NOTICE, expect.any(String));
    expect(useSharedDemoStore.getState().noticeHidden).toBe(true);
    expect(() => useSharedDemoStore.getState().setNoticeHidden(false)).not.toThrow();
    expect(useSharedDemoStore.getState().noticeHidden).toBe(false);
    setItem.mockRestore();
  });

  it("starts from the default when storage cannot be read", async () => {
    sessionStorage.setItem(STORAGE_KEYS.SHARED_DEMO_NOTICE, '{"state":{"noticeHidden":true},"version":0}');
    const getItem = vi.spyOn(window.sessionStorage, "getItem").mockImplementation(() => {
      throw new DOMException("blocked", "SecurityError");
    });

    await expect(useSharedDemoStore.persist.rehydrate()).resolves.toBeUndefined();
    expect(useSharedDemoStore.getState().noticeHidden).toBe(false);
    getItem.mockRestore();
  });

  it("clears quietly when storage cannot be written", () => {
    const removeItem = vi.spyOn(window.sessionStorage, "removeItem").mockImplementation(() => {
      throw new DOMException("blocked", "SecurityError");
    });

    expect(() => useSharedDemoStore.persist.clearStorage()).not.toThrow();
    removeItem.mockRestore();
  });
});
