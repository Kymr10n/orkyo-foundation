import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DetailDrawer } from "./DetailDrawer";

function setViewport(width: number) {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    configurable: true,
    value: vi.fn((query: string) => {
      const min = /\(min-width:\s*(\d+)px\)/.exec(query);
      return {
        matches: min ? width >= Number(min[1]) : false,
        media: query,
        onchange: null,
        addEventListener: () => {},
        removeEventListener: () => {},
        dispatchEvent: () => false,
      } as unknown as MediaQueryList;
    }),
  });
}

const originalMatchMedia = window.matchMedia;
afterEach(() => {
  Object.defineProperty(window, "matchMedia", {
    value: originalMatchMedia,
    writable: true,
    configurable: true,
  });
});

function renderDrawer(onOpenChange = vi.fn()) {
  return render(
    <DetailDrawer
      open
      onOpenChange={onOpenChange}
      title="Request details"
      description="Read-only view"
      footer={<button type="button">Close panel</button>}
    >
      <p>Detail body</p>
    </DetailDrawer>,
  );
}

describe("DetailDrawer", () => {
  it("slides in from the right on tablet", () => {
    setViewport(900);
    renderDrawer();
    const content = screen.getByRole("dialog");
    expect(content.className).toContain("right-0");
    expect(content.className).not.toContain("bottom-0");
    expect(screen.getByText("Request details")).toBeInTheDocument();
    expect(screen.getByText("Detail body")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Close panel" })).toBeInTheDocument();
  });

  it("slides up from the bottom on phone", () => {
    setViewport(500);
    renderDrawer();
    const content = screen.getByRole("dialog");
    expect(content.className).toContain("bottom-0");
    expect(content.className).not.toContain("right-0");
  });

  it("calls onOpenChange when dismissed", async () => {
    setViewport(900);
    const onOpenChange = vi.fn();
    renderDrawer(onOpenChange);
    // The built-in dismiss affordance (X), named exactly "Close" — distinct from
    // the "Close panel" footer button.
    await userEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
