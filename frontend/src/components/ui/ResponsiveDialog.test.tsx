import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ResponsiveDialog } from "./ResponsiveDialog";

/** Statically pin the viewport width for a render (no resize during the test). */
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

function renderDialog() {
  return render(
    <ResponsiveDialog
      open
      onOpenChange={vi.fn()}
      title="Edit request"
      description="Update the details"
      footer={<button type="button">Save</button>}
    >
      <p>Form body</p>
    </ResponsiveDialog>,
  );
}

describe("ResponsiveDialog", () => {
  it("renders a centered Dialog on desktop", () => {
    setViewport(1280);
    renderDialog();
    const content = screen.getByRole("dialog");
    expect(content.className).toContain("translate-x-");
    expect(content.className).not.toContain("bottom-0");
    expect(screen.getByText("Edit request")).toBeInTheDocument();
    expect(screen.getByText("Form body")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeInTheDocument();
  });

  it("renders a centered Dialog on tablet", () => {
    setViewport(900);
    renderDialog();
    expect(screen.getByRole("dialog").className).toContain("translate-x-");
  });

  it("renders a bottom Sheet on phone", () => {
    setViewport(500);
    renderDialog();
    const content = screen.getByRole("dialog");
    expect(content.className).toContain("bottom-0");
    expect(content.className).not.toContain("translate-x-");
    // Same content is reachable in the phone presentation.
    expect(screen.getByText("Edit request")).toBeInTheDocument();
    expect(screen.getByText("Form body")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeInTheDocument();
  });
});
