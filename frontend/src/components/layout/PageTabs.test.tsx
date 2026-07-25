import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { PageTabs } from "./PageTabs";

const tabs = [
  { value: "a", label: "Tab A" },
  { value: "b", label: "Tab B" },
];

describe("PageTabs", () => {
  it("renders an optional toolbar alongside the tab content", () => {
    render(
      <PageTabs tabs={tabs} value="a" onChange={vi.fn()} toolbar={<span>my-toolbar</span>}>
        <div>content</div>
      </PageTabs>,
    );

    expect(screen.getByText("my-toolbar")).toBeInTheDocument();
    expect(screen.getByText("content")).toBeInTheDocument();
  });

  it("renders no toolbar row when the toolbar prop is omitted", () => {
    render(
      <PageTabs tabs={tabs} value="a" onChange={vi.fn()}>
        <div>content</div>
      </PageTabs>,
    );

    expect(screen.queryByText("my-toolbar")).toBeNull();
  });
});
