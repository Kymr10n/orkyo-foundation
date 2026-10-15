import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { STORAGE_KEYS } from "@foundation/src/constants/storage";
import { useSharedDemoStore } from "@foundation/src/store/shared-demo-store";

describe("useSharedDemoStore", () => {
  beforeEach(() => {
    sessionStorage.clear();
    useSharedDemoStore.setState({ noticeHidden: false });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

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
  });
});
