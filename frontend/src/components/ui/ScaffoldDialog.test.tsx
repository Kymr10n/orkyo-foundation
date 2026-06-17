import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ScaffoldDialog } from "./ScaffoldDialog";

function renderScaffold(
  props: Partial<React.ComponentProps<typeof ScaffoldDialog>> = {},
) {
  const onOpenChange = vi.fn();
  render(
    <ScaffoldDialog open onOpenChange={onOpenChange} title="Edit person" {...props}>
      <div>body content</div>
    </ScaffoldDialog>,
  );
  return { onOpenChange };
}

describe("ScaffoldDialog", () => {
  it("renders the title, optional description, and children", () => {
    renderScaffold({ description: "Update the person details" });
    expect(screen.getByText("Edit person")).toBeInTheDocument();
    expect(screen.getByText("Update the person details")).toBeInTheDocument();
    expect(screen.getByText("body content")).toBeInTheDocument();
  });

  it("defaults to the lg width token", () => {
    renderScaffold();
    expect(screen.getByRole("dialog")).toHaveClass("max-w-2xl");
  });

  it("applies the requested size token", () => {
    renderScaffold({ size: "xl" });
    expect(screen.getByRole("dialog")).toHaveClass("max-w-3xl");
  });

  it("renders the description visually hidden when srOnlyDescription is set", () => {
    renderScaffold({ description: "Hidden but announced", srOnlyDescription: true });
    expect(screen.getByText("Hidden but announced")).toHaveClass("sr-only");
  });

  it("forwards contentProps onto DialogContent", () => {
    const onOpenAutoFocus = vi.fn();
    renderScaffold({ contentProps: { onOpenAutoFocus } });
    expect(onOpenAutoFocus).toHaveBeenCalled();
  });
});
