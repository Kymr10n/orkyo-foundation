import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";

// Breakpoint is mocked so the phone presentation is deterministic (the real hook
// reads matchMedia). Defaults to desktop; flip per-test.
let mockIsPhone = false;
vi.mock("@foundation/src/hooks/useBreakpoint", () => ({
  useBreakpoint: () => ({
    isPhone: mockIsPhone,
    isTablet: false,
    isDesktop: !mockIsPhone,
    device: mockIsPhone ? "phone" : "desktop",
  }),
}));

import { ScaffoldDialog } from "./ScaffoldDialog";

afterEach(() => {
  mockIsPhone = false;
});

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
    expect(screen.getByRole("dialog")).toHaveClass("sm:max-w-2xl");
  });

  it("applies the requested size token", () => {
    renderScaffold({ size: "xl" });
    expect(screen.getByRole("dialog")).toHaveClass("sm:max-w-3xl");
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

  it("takes over the whole screen on a phone instead of floating as a card", () => {
    mockIsPhone = true;
    renderScaffold();
    const content = screen.getByRole("dialog");
    expect(content).toHaveClass("inset-0", "h-[100dvh]", "max-w-none");
    // The width token must not survive alongside it, or the card comes back.
    expect(content).not.toHaveClass("sm:max-w-2xl");
  });
});
