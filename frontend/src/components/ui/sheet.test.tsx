import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "./sheet";

function renderSheet(side: "top" | "bottom" | "left" | "right", onOpenChange = vi.fn()) {
  return render(
    <Sheet open onOpenChange={onOpenChange}>
      <SheetContent side={side}>
        <SheetHeader>
          <SheetTitle>Panel title</SheetTitle>
          <SheetDescription>Panel description</SheetDescription>
        </SheetHeader>
        <p>Body content</p>
      </SheetContent>
    </Sheet>,
  );
}

describe("Sheet", () => {
  it("renders header, description and body when open", () => {
    renderSheet("right");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText("Panel title")).toBeInTheDocument();
    expect(screen.getByText("Panel description")).toBeInTheDocument();
    expect(screen.getByText("Body content")).toBeInTheDocument();
  });

  it("anchors to the requested side", () => {
    renderSheet("bottom");
    // The bottom variant pins to the viewport bottom edge.
    expect(screen.getByRole("dialog").className).toContain("bottom-0");
  });

  it("anchors to the right by default styling for side=right", () => {
    renderSheet("right");
    expect(screen.getByRole("dialog").className).toContain("right-0");
  });

  it("invokes onOpenChange when the close button is clicked", async () => {
    const onOpenChange = vi.fn();
    renderSheet("right", onOpenChange);
    await userEvent.click(screen.getByRole("button", { name: /close/i }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
