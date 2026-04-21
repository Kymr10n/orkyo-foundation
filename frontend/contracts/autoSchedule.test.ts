import {
  formatSolverKind,
  SchedulingReasonLabels,
  type SchedulingReasonCode,
} from "./autoSchedule";
import { describe, expect, it } from "vitest";

describe("Auto-schedule contract", () => {
  it("covers every scheduling reason code with a label", () => {
    const allCodes: SchedulingReasonCode[] = [
      "None",
      "NoCompatibleSpace",
      "DateWindowTooTight",
      "InsufficientCapacity",
      "BlockedByFixedAssignments",
      "InvalidDuration",
      "MissingRequiredData",
      "InternalSolverLimit",
    ];

    expect(Object.keys(SchedulingReasonLabels).sort()).toEqual(
      [...allCodes].sort(),
    );
  });

  it("keeps user-facing labels stable for known reason codes", () => {
    expect(SchedulingReasonLabels.NoCompatibleSpace).toBe(
      "No compatible space",
    );
    expect(SchedulingReasonLabels.BlockedByFixedAssignments).toBe(
      "Blocked by existing assignments",
    );
    expect(SchedulingReasonLabels.InternalSolverLimit).toBe(
      "Solver limit reached",
    );
  });

  it("formats solver kind for display", () => {
    expect(formatSolverKind("OrToolsCpSat")).toBe("OR-Tools CP-SAT");
    expect(formatSolverKind("Greedy")).toBe("Greedy");
  });
});
