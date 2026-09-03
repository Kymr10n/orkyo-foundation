import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ConflictDetailsPopover } from "./ConflictDetailsPopover";
import type { Conflict, Request } from "@foundation/src/types/requests";

const request = {
  id: "req-1",
  name: "Bracket run",
  startTs: "2026-09-08T08:00:00Z",
  endTs: "2026-09-08T12:00:00Z",
} as Request;

const peer = { id: "req-2", name: "Housing batch" } as Request;

function conflict(overrides: Partial<Conflict> = {}) {
  return {
    id: "c-1",
    kind: "overlap",
    severity: "error",
    message: "Overlaps Housing batch on PMF Mill VMC-1",
    peerRequestId: "req-2",
    ...overrides,
  } as Conflict;
}

function renderPopover(overrides: Partial<Parameters<typeof ConflictDetailsPopover>[0]> = {}) {
  const onOpenRequest = vi.fn();
  const onClose = vi.fn();
  render(
    <ConflictDetailsPopover
      request={request}
      conflicts={[{ ...conflict(), request }]}
      position={{ x: 40, y: 20 }}
      onOpenRequest={onOpenRequest}
      onClose={onClose}
      peerRequestFor={() => peer}
      {...overrides}
    />,
  );
  return { onOpenRequest, onClose };
}

describe("ConflictDetailsPopover", () => {
  it("answers the question a red bar asks: what is the conflict", () => {
    // The grid used to convey "in conflict" through colour and a count only.
    renderPopover();

    expect(screen.getByText("Overlaps Housing batch on PMF Mill VMC-1")).toBeInTheDocument();
    expect(screen.getByText("Scheduling Overlap")).toBeInTheDocument();
  });

  it("lists every conflict on the request", () => {
    renderPopover({
      conflicts: [
        { ...conflict(), request },
        { ...conflict({ id: "c-2", kind: "capacity_exceeded", message: "Over capacity" }), request },
      ],
    });

    expect(screen.getByText("Scheduling Overlap")).toBeInTheDocument();
    expect(screen.getByText("Capacity Exceeded")).toBeInTheDocument();
  });

  it("opens the request behind the conflict and closes itself", async () => {
    const { onOpenRequest, onClose } = renderPopover();

    await userEvent.click(screen.getByText("Bracket run"));

    expect(onOpenRequest).toHaveBeenCalledWith(request);
    expect(onClose).toHaveBeenCalled();
  });

  it("reaches the request on the other side of an overlap", async () => {
    // Resolving an overlap usually means moving the other job, so it has to be one click away.
    const { onOpenRequest } = renderPopover();

    await userEvent.click(screen.getByText(/View other request: Housing batch/));

    expect(onOpenRequest).toHaveBeenCalledWith(peer);
  });
});
