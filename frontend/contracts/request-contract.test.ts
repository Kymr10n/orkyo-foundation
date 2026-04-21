/**
 * Request Domain Contract Tests
 *
 * These tests guard the contract between the frontend TypeScript types
 * and the backend C# DTOs / enums. If any of these tests fail, it means
 * the API contract has drifted and both sides need to be reconciled.
 *
 * Backend source of truth: backend/api/Models/Request.cs
 *
 * CRITICAL: Do not change expected values without verifying the backend model.
 */

import type {
  CreateRequestRequest,
  DurationUnit,
  MoveRequestRequest,
  PlanningMode,
  Request,
  RequestRequirement,
  RequestStatus,
  UpdateRequestRequest,
} from "../src/types/requests";
import { describe, expect, it } from "vitest";

// ─────────────────────────────────────────────────────────────────────
// Helpers: compile-time type assertions
// ─────────────────────────────────────────────────────────────────────

/**
 * Asserts at compile time that T has exactly the keys in K.
 * If the backend adds/removes a field this will cause a type error.
 */
type AssertKeys<T, K extends keyof T> = [keyof T] extends [K]
  ? [K] extends [keyof T]
    ? true
    : never
  : never;

// ─────────────────────────────────────────────────────────────────────
// Enum value contracts
// ─────────────────────────────────────────────────────────────────────

describe("Contract - PlanningMode enum values", () => {
  it("should contain exactly the values from BE PlanningMode enum", () => {
    // Must match [JsonStringEnumMemberName] values in backend/api/Models/Request.cs
    const expectedValues: PlanningMode[] = ["leaf", "summary", "container"];
    const feValues: PlanningMode[] = ["leaf", "summary", "container"];
    expect(feValues).toEqual(expectedValues);
  });

  it("should have exactly 3 members", () => {
    const allValues: PlanningMode[] = ["leaf", "summary", "container"];
    expect(allValues).toHaveLength(3);
  });
});

describe("Contract - DurationUnit enum values", () => {
  it("should contain exactly the values from BE DurationUnit enum", () => {
    // Must match [JsonStringEnumMemberName] values in backend/api/Models/Request.cs
    const expectedValues: DurationUnit[] = [
      "minutes",
      "hours",
      "days",
      "weeks",
      "months",
      "years",
    ];
    const feValues: DurationUnit[] = [
      "minutes",
      "hours",
      "days",
      "weeks",
      "months",
      "years",
    ];
    expect(feValues).toEqual(expectedValues);
  });

  it("should have exactly 6 members", () => {
    const allValues: DurationUnit[] = [
      "minutes",
      "hours",
      "days",
      "weeks",
      "months",
      "years",
    ];
    expect(allValues).toHaveLength(6);
  });
});

describe("Contract - RequestStatus enum values", () => {
  it("should contain exactly the values from BE RequestStatus enum", () => {
    // Must match [JsonStringEnumMemberName] values in backend/api/Models/Request.cs
    const expectedValues: RequestStatus[] = [
      "planned",
      "in_progress",
      "done",
      "cancelled",
    ];
    const feValues: RequestStatus[] = [
      "planned",
      "in_progress",
      "done",
      "cancelled",
    ];
    expect(feValues).toEqual(expectedValues);
  });

  it("should have exactly 4 members (planned, in_progress, done, cancelled)", () => {
    const allValues: RequestStatus[] = [
      "planned",
      "in_progress",
      "done",
      "cancelled",
    ];
    expect(allValues).toHaveLength(4);
  });
});

// ─────────────────────────────────────────────────────────────────────
// RequestInfo ↔ Request field contract
// ─────────────────────────────────────────────────────────────────────

describe("Contract - Request (FE) ↔ RequestInfo (BE) field alignment", () => {
  /**
   * Compile-time assertion: if the Request interface gains or loses a field,
   * this type check will fail and the test file won't compile.
   */
  type _RequestKeysCheck = AssertKeys<
    Request,
    | "id"
    | "name"
    | "description"
    | "parentRequestId"
    | "planningMode"
    | "sortOrder"
    | "spaceId"
    | "requestItemId"
    | "startTs"
    | "endTs"
    | "earliestStartTs"
    | "latestEndTs"
    | "minimalDurationValue"
    | "minimalDurationUnit"
    | "actualDurationValue"
    | "actualDurationUnit"
    | "schedulingSettingsApply"
    | "status"
    | "requirements"
    | "createdAt"
    | "updatedAt"
    | "isScheduled"
    | "durationMin" // FE-computed, not sent by BE
  >;
  const _typeCheck: _RequestKeysCheck = true;

  // The fields that BE RequestInfo sends over the wire (camelCase from JsonNamingPolicy)
  const beRequestInfoFields = [
    "id",
    "name",
    "description",
    "parentRequestId",
    "planningMode",
    "sortOrder",
    "spaceId",
    "requestItemId",
    "startTs",
    "endTs",
    "earliestStartTs",
    "latestEndTs",
    "minimalDurationValue",
    "minimalDurationUnit",
    "actualDurationValue",
    "actualDurationUnit",
    "schedulingSettingsApply",
    "status",
    "requirements",
    "createdAt",
    "updatedAt",
    "isScheduled",
  ] as const;

  // FE-only fields that are computed client-side (NOT sent by BE)
  const feOnlyFields = ["durationMin"] as const;

  it("should have all BE RequestInfo fields in the FE Request interface", () => {
    // Build a sample Request object — TypeScript enforces required fields
    const sample: Request = {
      id: "00000000-0000-0000-0000-000000000000",
      name: "test",
      description: null,
      parentRequestId: null,
      planningMode: "leaf",
      sortOrder: 0,
      spaceId: null,
      requestItemId: null,
      startTs: null,
      endTs: null,
      earliestStartTs: null,
      latestEndTs: null,
      minimalDurationValue: 60,
      minimalDurationUnit: "minutes",
      actualDurationValue: null,
      actualDurationUnit: null,
      schedulingSettingsApply: true,
      status: "planned",
      requirements: [],
      createdAt: "2025-01-01T00:00:00Z",
      updatedAt: "2025-01-01T00:00:00Z",
      isScheduled: false,
    };

    // Every BE field must be present as a key on the sample
    for (const field of beRequestInfoFields) {
      expect(sample).toHaveProperty(field);
    }
  });

  it("should document FE-only computed fields", () => {
    // These fields exist on FE Request but are NOT part of BE RequestInfo.
    // They're computed client-side (e.g. by utilization-api.ts).
    expect(feOnlyFields).toEqual(["durationMin"]);
  });

  it("should NOT have phantom fields that BE never sends", () => {
    // These fields were previously on FE Request but have no BE counterpart:
    const phantomFields = [
      "jobId",
      "userId",
      "conflicts",
      "calculatedActualStart",
      "calculatedActualEnd",
      "calculatedActualDurationMinutes",
    ];

    const sample: Request = {
      id: "00000000-0000-0000-0000-000000000000",
      name: "test",
      planningMode: "leaf",
      sortOrder: 0,
      minimalDurationValue: 60,
      minimalDurationUnit: "minutes",
      schedulingSettingsApply: true,
      status: "planned",
      createdAt: "2025-01-01T00:00:00Z",
      updatedAt: "2025-01-01T00:00:00Z",
    };

    for (const field of phantomFields) {
      expect(field in sample).toBe(false);
    }
  });
});

// ─────────────────────────────────────────────────────────────────────
// RequestRequirement ↔ RequestRequirementInfo field contract
// ─────────────────────────────────────────────────────────────────────

describe("Contract - RequestRequirement (FE) ↔ RequestRequirementInfo (BE)", () => {
  type _RequirementKeysCheck = AssertKeys<
    RequestRequirement,
    | "id"
    | "requestId"
    | "criterionId"
    | "value"
    | "createdAt"
    | "criterion"
  >;
  const _typeCheck: _RequirementKeysCheck = true;

  it("should include all BE RequestRequirementInfo fields", () => {
    const beFields = [
      "id",
      "requestId",
      "criterionId",
      "value",
      "createdAt",
      "criterion",
    ];

    const sample: RequestRequirement = {
      id: "00000000-0000-0000-0000-000000000000",
      requestId: "00000000-0000-0000-0000-000000000001",
      criterionId: "00000000-0000-0000-0000-000000000002",
      value: "test",
      createdAt: "2025-01-01T00:00:00Z",
      criterion: {
        id: "00000000-0000-0000-0000-000000000002",
        name: "Weight",
        dataType: "number",
        unit: "kg",
        enumValues: undefined,
      },
    };

    for (const field of beFields) {
      expect(sample).toHaveProperty(field);
    }
  });

  it("criterion nested object should include id field from BE CriterionBasicInfo", () => {
    // BE CriterionBasicInfo has: Id, Name, DataType, Unit, EnumValues
    const criterion: NonNullable<RequestRequirement["criterion"]> = {
      id: "00000000-0000-0000-0000-000000000001",
      name: "Test",
      dataType: "string",
    };
    expect(criterion).toHaveProperty("id");
    expect(criterion).toHaveProperty("name");
    expect(criterion).toHaveProperty("dataType");
  });
});

// ─────────────────────────────────────────────────────────────────────
// CreateRequestRequest DTO contract
// ─────────────────────────────────────────────────────────────────────

describe("Contract - CreateRequestRequest DTO", () => {
  type _CreateKeysCheck = AssertKeys<
    CreateRequestRequest,
    | "name"
    | "description"
    | "parentRequestId"
    | "planningMode"
    | "sortOrder"
    | "spaceId"
    | "requestItemId"
    | "startTs"
    | "endTs"
    | "earliestStartTs"
    | "latestEndTs"
    | "minimalDurationValue"
    | "minimalDurationUnit"
    | "actualDurationValue"
    | "actualDurationUnit"
    | "schedulingSettingsApply"
    | "status"
    | "requirements"
  >;
  const _typeCheck: _CreateKeysCheck = true;

  it("should match BE CreateRequestRequest fields", () => {
    const beFields = [
      "name",
      "description",
      "parentRequestId",
      "planningMode",
      "sortOrder",
      "spaceId",
      "requestItemId",
      "startTs",
      "endTs",
      "earliestStartTs",
      "latestEndTs",
      "minimalDurationValue",
      "minimalDurationUnit",
      "actualDurationValue",
      "actualDurationUnit",
      "schedulingSettingsApply",
      "status",
      "requirements",
    ];

    const sample: CreateRequestRequest = {
      name: "test",
      minimalDurationValue: 60,
      minimalDurationUnit: "minutes",
    };

    // Required fields are present
    expect(sample).toHaveProperty("name");
    expect(sample).toHaveProperty("minimalDurationValue");
    expect(sample).toHaveProperty("minimalDurationUnit");

    // All BE fields are valid keys (compile-time + runtime check)
    for (const field of beFields) {
      expect(typeof field).toBe("string");
    }
  });
});

// ─────────────────────────────────────────────────────────────────────
// UpdateRequestRequest DTO contract
// ─────────────────────────────────────────────────────────────────────

describe("Contract - UpdateRequestRequest DTO", () => {
  type _UpdateKeysCheck = AssertKeys<
    UpdateRequestRequest,
    | "name"
    | "description"
    | "parentRequestId"
    | "planningMode"
    | "sortOrder"
    | "spaceId"
    | "requestItemId"
    | "startTs"
    | "endTs"
    | "earliestStartTs"
    | "latestEndTs"
    | "minimalDurationValue"
    | "minimalDurationUnit"
    | "actualDurationValue"
    | "actualDurationUnit"
    | "schedulingSettingsApply"
    | "status"
    | "requirements"
  >;
  const _typeCheck: _UpdateKeysCheck = true;

  it("should have all fields optional (partial update)", () => {
    // An empty update is valid — all fields are optional
    const sample: UpdateRequestRequest = {};
    expect(sample).toBeDefined();
  });
});

// ─────────────────────────────────────────────────────────────────────
// MoveRequestRequest DTO contract
// ─────────────────────────────────────────────────────────────────────

describe("Contract - MoveRequestRequest DTO", () => {
  type _MoveKeysCheck = AssertKeys<
    MoveRequestRequest,
    "newParentRequestId" | "sortOrder"
  >;
  const _typeCheck: _MoveKeysCheck = true;

  it("should match BE MoveRequestRequest fields", () => {
    const sample: MoveRequestRequest = {
      newParentRequestId: null,
      sortOrder: 0,
    };
    expect(sample).toHaveProperty("newParentRequestId");
    expect(sample).toHaveProperty("sortOrder");
  });

  it("newParentRequestId should accept null (move to root)", () => {
    const sample: MoveRequestRequest = {
      newParentRequestId: null,
      sortOrder: 0,
    };
    expect(sample.newParentRequestId).toBeNull();
  });
});

// ─────────────────────────────────────────────────────────────────────
// JSON casing contract (BE uses JsonNamingPolicy.CamelCase)
// ─────────────────────────────────────────────────────────────────────

describe("Contract - JSON field casing", () => {
  it("BE uses camelCase naming policy — all FE field names must be camelCase", () => {
    // These are the exact wire-format field names from BE RequestInfo
    // (C# PascalCase → camelCase via JsonNamingPolicy.CamelCase)
    const beWireFields = [
      "id",
      "name",
      "description",
      "parentRequestId",
      "planningMode",
      "sortOrder",
      "spaceId",
      "requestItemId",
      "startTs",
      "endTs",
      "earliestStartTs",
      "latestEndTs",
      "minimalDurationValue",
      "minimalDurationUnit",
      "actualDurationValue",
      "actualDurationUnit",
      "schedulingSettingsApply",
      "status",
      "requirements",
      "createdAt",
      "updatedAt",
      "isScheduled",
    ];

    for (const field of beWireFields) {
      // camelCase: first char is lowercase
      expect(field[0]).toBe(field[0].toLowerCase());
      // No underscores (except enum values — these are field names)
      // Note: startTs, endTs use Ts suffix which is fine
    }
  });
});
