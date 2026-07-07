import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ScheduleToDialog } from "./ScheduleToDialog";
import { makeRequest } from "@foundation/src/test-utils/request-fixtures";
import type { Space } from "@foundation/src/types/space";

function makeSpace(id: string, name: string): Space {
  return {
    id,
    siteId: "site-1",
    name,
    isPhysical: true,
    capacity: 1,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
  };
}

const spaces = [makeSpace("space-1", "Room A"), makeSpace("space-2", "Room B")];
// Local-time start; the picker round-trips via a local "YYYY-MM-DDTHH:mm" string.
const defaultStart = new Date(2026, 3, 17, 9, 0);

function renderDialog(
  props: Partial<React.ComponentProps<typeof ScheduleToDialog>> = {},
) {
  const onOpenChange = vi.fn();
  const onSchedule = vi.fn().mockResolvedValue(undefined);
  render(
    <ScheduleToDialog
      open
      onOpenChange={onOpenChange}
      request={makeRequest({ id: "u-1", name: "Backlog Task" })}
      spaces={spaces}
      onSchedule={onSchedule}
      defaultStart={defaultStart}
      {...props}
    />,
  );
  return { onOpenChange, onSchedule };
}

describe("ScheduleToDialog", () => {
  it("shows the request name in the title", () => {
    renderDialog();
    expect(screen.getByText(/Backlog Task/)).toBeInTheDocument();
  });

  it("disables Schedule until a space is selected", () => {
    renderDialog();
    expect(screen.getByRole("button", { name: /^schedule$/i })).toBeDisabled();
  });

  it("lists the provided spaces", async () => {
    renderDialog();
    await userEvent.click(screen.getByRole("combobox"));
    expect(await screen.findByRole("option", { name: "Room A" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Room B" })).toBeInTheDocument();
  });

  it("schedules with the chosen space + start time, then closes", async () => {
    const { onSchedule, onOpenChange } = renderDialog();

    // Pick a space via the Radix select.
    await userEvent.click(screen.getByRole("combobox"));
    await userEvent.click(await screen.findByRole("option", { name: "Room B" }));

    const scheduleBtn = screen.getByRole("button", { name: /^schedule$/i });
    expect(scheduleBtn).toBeEnabled();
    await userEvent.click(scheduleBtn);

    expect(onSchedule).toHaveBeenCalledTimes(1);
    const [resourceId, startTs] = onSchedule.mock.calls[0];
    expect(resourceId).toBe("space-2");
    expect(startTs).toBeInstanceOf(Date);
    expect((startTs as Date).getTime()).toBe(defaultStart.getTime());

    // Resolves → the dialog requests to close.
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("keeps the dialog open when scheduling fails", async () => {
    const onSchedule = vi.fn().mockRejectedValue(new Error("conflict"));
    const { onOpenChange } = renderDialog({ onSchedule });

    await userEvent.click(screen.getByRole("combobox"));
    await userEvent.click(await screen.findByRole("option", { name: "Room A" }));
    await userEvent.click(screen.getByRole("button", { name: /^schedule$/i }));

    expect(onSchedule).toHaveBeenCalledTimes(1);
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
