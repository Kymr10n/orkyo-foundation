import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ScheduleSlotDialog } from "./ScheduleSlotDialog";
import { makeRequest } from "@foundation/src/test-utils/request-fixtures";

const selection = {
  start: new Date("2026-04-17T09:00:00Z"),
  end: new Date("2026-04-17T11:00:00Z"),
};

function renderDialog(props: Partial<React.ComponentProps<typeof ScheduleSlotDialog>> = {}) {
  const onOpenChange = vi.fn();
  const onCreateNew = vi.fn();
  const onScheduleExisting = vi.fn();
  render(
    <ScheduleSlotDialog
      open
      onOpenChange={onOpenChange}
      selection={selection}
      backlog={[]}
      onCreateNew={onCreateNew}
      onScheduleExisting={onScheduleExisting}
      {...props}
    />,
  );
  return { onOpenChange, onCreateNew, onScheduleExisting };
}

describe("ScheduleSlotDialog", () => {
  it("shows the selected slot range in the header", () => {
    renderDialog();
    // Range label is rendered as the dialog description.
    expect(screen.getByText(/–/)).toBeInTheDocument();
  });

  it("invokes onCreateNew when 'Create new request' is clicked", async () => {
    const { onCreateNew } = renderDialog();
    await userEvent.click(screen.getByRole("button", { name: /create new request/i }));
    expect(onCreateNew).toHaveBeenCalledTimes(1);
  });

  it("shows an empty-state message and no picker when the backlog is empty", () => {
    renderDialog({ backlog: [] });
    expect(screen.getByText(/no unscheduled requests/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^schedule$/i })).not.toBeInTheDocument();
  });

  it("renders the picker with a disabled Schedule button when backlog has items", () => {
    renderDialog({ backlog: [makeRequest({ name: "Pending task" })] });
    const scheduleBtn = screen.getByRole("button", { name: /^schedule$/i });
    expect(scheduleBtn).toBeDisabled();
  });

  it("closes via Cancel", async () => {
    const { onOpenChange } = renderDialog();
    await userEvent.click(screen.getByRole("button", { name: /cancel/i }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
