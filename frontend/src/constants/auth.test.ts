import { describe, it, expect } from "vitest";
import {
  AUTH_STAGES,
  LOADING_STAGES,
  UNAUTHENTICATED_STAGES,
  TENANT_STATUS,
  SUSPENSION_REASON,
  AUTH_EVENTS,
  AUTH_MESSAGES,
} from "./auth";
import type { AuthStage } from "./auth";

// ── AUTH_STAGES ───────────────────────────────────────────────────────────────

describe("AUTH_STAGES", () => {
  it("contains all 12 expected stages", () => {
    expect(Object.keys(AUTH_STAGES)).toHaveLength(12);
  });

  it.each([
    ["INITIALIZING", "initializing"],
    ["UNAUTHENTICATED", "unauthenticated"],
    ["TOS_REQUIRED", "tos_required"],
    ["NO_TENANTS", "no_tenants"],
    ["NO_TENANTS_ADMIN", "no_tenants_admin"],
    ["SELECTING_TENANT", "selecting_tenant"],
    ["REDIRECTING_TO_TENANT", "redirecting_to_tenant"],
    ["REDIRECTING_LOGIN", "redirecting_login"],
    ["LOGGING_OUT", "logging_out"],
    ["READY", "ready"],
    ["ERROR_BACKEND", "error_backend"],
    ["ERROR_NETWORK", "error_network"],
  ] as const)("AUTH_STAGES.%s = '%s'", (key, value) => {
    expect(AUTH_STAGES[key]).toBe(value);
  });

  it("values are all unique", () => {
    const values = Object.values(AUTH_STAGES);
    expect(new Set(values).size).toBe(values.length);
  });
});

// ── LOADING_STAGES ────────────────────────────────────────────────────────────

describe("LOADING_STAGES", () => {
  it("includes initializing, logging_out, redirecting_login", () => {
    expect(LOADING_STAGES.has(AUTH_STAGES.INITIALIZING)).toBe(true);
    expect(LOADING_STAGES.has(AUTH_STAGES.LOGGING_OUT)).toBe(true);
    expect(LOADING_STAGES.has(AUTH_STAGES.REDIRECTING_LOGIN)).toBe(true);
  });

  it("excludes ready and error stages", () => {
    expect(LOADING_STAGES.has(AUTH_STAGES.READY)).toBe(false);
    expect(LOADING_STAGES.has(AUTH_STAGES.ERROR_BACKEND)).toBe(false);
    expect(LOADING_STAGES.has(AUTH_STAGES.ERROR_NETWORK)).toBe(false);
  });

  it("has exactly 3 members", () => {
    expect(LOADING_STAGES.size).toBe(3);
  });

  it("is a subset of UNAUTHENTICATED_STAGES", () => {
    for (const stage of LOADING_STAGES) {
      expect(UNAUTHENTICATED_STAGES.has(stage)).toBe(true);
    }
  });
});

// ── UNAUTHENTICATED_STAGES ───────────────────────────────────────────────────

describe("UNAUTHENTICATED_STAGES", () => {
  const expectedUnauthenticated: AuthStage[] = [
    AUTH_STAGES.INITIALIZING,
    AUTH_STAGES.UNAUTHENTICATED,
    AUTH_STAGES.LOGGING_OUT,
    AUTH_STAGES.ERROR_BACKEND,
    AUTH_STAGES.ERROR_NETWORK,
    AUTH_STAGES.REDIRECTING_LOGIN,
  ];

  it("has exactly 6 members", () => {
    expect(UNAUTHENTICATED_STAGES.size).toBe(6);
  });

  it.each(expectedUnauthenticated)("includes '%s'", (stage) => {
    expect(UNAUTHENTICATED_STAGES.has(stage)).toBe(true);
  });

  it("excludes stages that require authentication", () => {
    const authenticatedStages: AuthStage[] = [
      AUTH_STAGES.READY,
      AUTH_STAGES.TOS_REQUIRED,
      AUTH_STAGES.NO_TENANTS,
      AUTH_STAGES.NO_TENANTS_ADMIN,
      AUTH_STAGES.SELECTING_TENANT,
      AUTH_STAGES.REDIRECTING_TO_TENANT,
    ];
    for (const stage of authenticatedStages) {
      expect(UNAUTHENTICATED_STAGES.has(stage)).toBe(false);
    }
  });

  it("every AUTH_STAGE is either authenticated or unauthenticated", () => {
    const allStages = Object.values(AUTH_STAGES);
    const authenticatedCount = allStages.filter(s => !UNAUTHENTICATED_STAGES.has(s)).length;
    const unauthenticatedCount = allStages.filter(s => UNAUTHENTICATED_STAGES.has(s)).length;
    expect(authenticatedCount + unauthenticatedCount).toBe(allStages.length);
  });
});

// ── TENANT_STATUS ─────────────────────────────────────────────────────────────

describe("TENANT_STATUS", () => {
  it("has 5 statuses", () => {
    expect(Object.keys(TENANT_STATUS)).toHaveLength(5);
  });

  it.each([
    ["ACTIVE", "active"],
    ["SUSPENDED", "suspended"],
    ["PENDING", "pending"],
    ["DELETED", "deleted"],
    ["DELETING", "deleting"],
  ] as const)("TENANT_STATUS.%s = '%s'", (key, value) => {
    expect(TENANT_STATUS[key]).toBe(value);
  });
});

// ── SUSPENSION_REASON ─────────────────────────────────────────────────────────

describe("SUSPENSION_REASON", () => {
  it("has 6 reasons", () => {
    expect(Object.keys(SUSPENSION_REASON)).toHaveLength(6);
  });

  it("values are all lowercase snake_case", () => {
    for (const value of Object.values(SUSPENSION_REASON)) {
      expect(value).toMatch(/^[a-z]+(_[a-z]+)*$/);
    }
  });
});

// ── AUTH_EVENTS ───────────────────────────────────────────────────────────────

describe("AUTH_EVENTS", () => {
  it("has 14 event types", () => {
    expect(Object.keys(AUTH_EVENTS)).toHaveLength(14);
  });

  it("values are all SCREAMING_SNAKE_CASE", () => {
    for (const value of Object.values(AUTH_EVENTS)) {
      expect(value).toMatch(/^[A-Z]+(_[A-Z]+)*$/);
    }
  });

  it("keys match values", () => {
    for (const [key, value] of Object.entries(AUTH_EVENTS)) {
      expect(key).toBe(value);
    }
  });
});

// ── AUTH_MESSAGES ──────────────────────────────────────────────────────────────

describe("AUTH_MESSAGES", () => {
  it("all values are non-empty strings", () => {
    for (const value of Object.values(AUTH_MESSAGES)) {
      expect(typeof value).toBe("string");
      expect(value.length).toBeGreaterThan(0);
    }
  });

  it("has messages for loading/redirecting states", () => {
    expect(AUTH_MESSAGES.LOADING).toBeDefined();
    expect(AUTH_MESSAGES.REDIRECTING).toBeDefined();
    expect(AUTH_MESSAGES.REDIRECTING_LOGIN).toBeDefined();
    expect(AUTH_MESSAGES.SIGNING_OUT).toBeDefined();
  });

  it("has messages for error states", () => {
    expect(AUTH_MESSAGES.BACKEND_ERROR_TITLE).toBeDefined();
    expect(AUTH_MESSAGES.BACKEND_ERROR_DETAIL).toBeDefined();
    expect(AUTH_MESSAGES.NETWORK_ERROR_TITLE).toBeDefined();
    expect(AUTH_MESSAGES.NETWORK_ERROR_DETAIL).toBeDefined();
  });
});
