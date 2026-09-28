import { describe, expect, it } from "vitest";
import { proposalToAutoScheduleRequestIds, proposalToRequestUpdate } from "./ai-proposal";
import type { AiProposal } from "@foundation/src/lib/api/ai-api";

const proposal: AiProposal = {
  toolUseId: "toolu_1",
  kind: "propose_update_request",
  input: JSON.stringify({
    requestId: "req-1",
    changes: { startTs: "2026-03-02T09:00:00Z", resourceIds: ["res-a", "res-b"] },
    rationale: "Studio A is free from 09:00.",
  }),
};

describe("proposalToRequestUpdate", () => {
  it("extracts the request id and the changed fields", () => {
    const result = proposalToRequestUpdate(proposal.input);

    expect(result.requestId).toBe("req-1");
    expect(result.changes).toEqual({
      startTs: "2026-03-02T09:00:00Z",
      resourceIds: ["res-a", "res-b"],
    });
  });

  it("yields no request id for an unparseable payload, so nothing is applied", () => {
    expect(proposalToRequestUpdate("{{{").requestId).toBeNull();
  });
});

describe("proposalToAutoScheduleRequestIds", () => {
  it("extracts the requests an auto-scheduling proposal names", () => {
    const input = JSON.stringify({
      requestIds: ["req-1", "req-2", "req-3"],
      rationale: "The solver can satisfy the criterion.",
    });

    expect(proposalToAutoScheduleRequestIds(input)).toEqual(["req-1", "req-2", "req-3"]);
  });

  it("yields nothing for an update proposal, which names no request set", () => {
    // The two kinds carry different payloads; reading one as the other must not half-work.
    expect(proposalToAutoScheduleRequestIds(proposal.input)).toEqual([]);
  });

  it("drops entries that are not usable ids", () => {
    const input = JSON.stringify({ requestIds: ["req-1", "", 42, null, "req-2"] });

    expect(proposalToAutoScheduleRequestIds(input)).toEqual(["req-1", "req-2"]);
  });

  it("yields nothing for an unparseable payload", () => {
    expect(proposalToAutoScheduleRequestIds("{{{")).toEqual([]);
  });
});
