import { describe, it, expect, beforeEach, vi, afterEach } from "vitest";
import { rememberSessionEndRedirect, sessionEndRedirect, isEphemeralSession } from "./session-end";

vi.mock("./tenant-navigation", () => ({
  getApexOrigin: () => "https://orkyo.com",
}));

describe("session-end", () => {
  beforeEach(() => sessionStorage.clear());
  afterEach(() => vi.restoreAllMocks());

  it("records the apex for an ephemeral session", () => {
    rememberSessionEndRedirect("demo");

    expect(sessionEndRedirect()).toBe("https://orkyo.com/");
    expect(isEphemeralSession()).toBe(true);
  });

  it("records nothing for an ordinary session", () => {
    rememberSessionEndRedirect(null);

    expect(sessionEndRedirect()).toBeNull();
    expect(isEphemeralSession()).toBe(false);
  });

  it("clears a stale marker when an ordinary session bootstraps in the same tab", () => {
    // Demo visitor logs out, then a real user signs in on the same tab: the real user must
    // still end at the login flow, not be silently dumped on the marketing site.
    rememberSessionEndRedirect("demo");
    rememberSessionEndRedirect(undefined);

    expect(sessionEndRedirect()).toBeNull();
  });

  it("gives every reader the same answer until the next bootstrap", () => {
    // A session ends in a burst of concurrent 401s; each handler must reach the same
    // destination, or the first one's marketing-site exit is overridden by the login flow.
    rememberSessionEndRedirect("demo");

    expect(sessionEndRedirect()).toBe("https://orkyo.com/");
    expect(sessionEndRedirect()).toBe("https://orkyo.com/");
  });

  it("stores a flag, not a URL, so storage cannot steer the redirect", () => {
    sessionStorage.setItem("orkyo:session-end-redirect", "https://evil.example/");

    expect(sessionEndRedirect()).toBe("https://orkyo.com/");
  });

  it("never throws when sessionStorage is unavailable", () => {
    // Private mode / quota. A worse demo ending is acceptable; a broken app is not.
    // Spy the instance, not Storage.prototype — jsdom's sessionStorage does not dispatch
    // through the prototype, so a prototype spy silently never fires.
    vi.spyOn(window.sessionStorage, "setItem").mockImplementation(() => {
      throw new Error("QuotaExceededError");
    });
    vi.spyOn(window.sessionStorage, "getItem").mockImplementation(() => {
      throw new Error("SecurityError");
    });

    expect(() => rememberSessionEndRedirect("demo")).not.toThrow();
    expect(sessionEndRedirect()).toBeNull();
  });
});
