import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ProposalCard, proposalToAutoScheduleRequestIds, proposalToRequestUpdate } from "./ProposalCard";
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

describe("ProposalCard", () => {
  it("shows the concrete values, not just the model's prose", () => {
    // What someone approves has to be what will actually be written.
    render(
      <ProposalCard proposal={proposal} canApply onApply={vi.fn()} onDecline={vi.fn()} />
    );

    expect(screen.getByText("Studio A is free from 09:00.")).toBeInTheDocument();
    expect(screen.getByText("New start")).toBeInTheDocument();
    expect(screen.getByText("res-a, res-b")).toBeInTheDocument();
  });

  it("offers Apply to someone who can edit", async () => {
    const onApply = vi.fn();
    render(
      <ProposalCard proposal={proposal} canApply onApply={onApply} onDecline={vi.fn()} />
    );

    await userEvent.click(screen.getByRole("button", { name: /apply/i }));

    expect(onApply).toHaveBeenCalledOnce();
  });

  it("hides Apply from someone who cannot edit, and says why", () => {
    render(
      <ProposalCard
        proposal={proposal}
        canApply={false}
        onApply={vi.fn()}
        onDecline={vi.fn()}
      />
    );

    expect(screen.queryByRole("button", { name: /apply/i })).not.toBeInTheDocument();
    expect(screen.getByText(/do not have permission/i)).toBeInTheDocument();
  });

  it("still shows something when the payload cannot be parsed", () => {
    // Better to show a person raw detail than to hide what they are approving.
    render(
      <ProposalCard
        proposal={{ ...proposal, input: "not json" }}
        canApply
        onApply={vi.fn()}
        onDecline={vi.fn()}
      />
    );

    expect(screen.getByText("not json")).toBeInTheDocument();
  });
});

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
