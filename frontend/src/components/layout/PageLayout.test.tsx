import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { PageLayout } from "./PageLayout";
import { PageHeader } from "./PageHeader";
import { PageTabs } from "./PageTabs";

describe("PageLayout", () => {
  it("wraps children in the standard page container classes", () => {
    const { container } = render(
      <PageLayout>
        <div data-testid="child" />
      </PageLayout>,
    );

    const outer = container.firstElementChild as HTMLElement;
    expect(outer.className).toContain("flex");
    expect(outer.className).toContain("flex-col");
    expect(outer.className).toContain("h-full");
    expect(outer.className).toContain("p-4");
    expect(outer.className).toContain("md:p-6");
    expect(outer.className).toContain("lg:p-8");
    expect(screen.getByTestId("child")).toBeInTheDocument();
  });
});

describe("PageHeader", () => {
  it("renders title and optional description", () => {
    render(<PageHeader title="Spaces" description="Manage spaces" />);
    const heading = screen.getByRole("heading", { level: 1, name: "Spaces" });
    expect(heading).toBeInTheDocument();
    expect(heading.className).toContain("text-2xl");
    expect(heading.className).toContain("font-bold");
    expect(screen.getByText("Manage spaces")).toBeInTheDocument();
  });

  it("renders actions when provided", () => {
    render(
      <PageHeader
        title="Utilization"
        actions={<button>Do thing</button>}
      />,
    );
    expect(screen.getByRole("button", { name: "Do thing" })).toBeInTheDocument();
  });

  it("omits description and actions when not provided", () => {
    render(<PageHeader title="Conflicts" />);
    expect(screen.getByRole("heading", { level: 1, name: "Conflicts" })).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });
});

describe("PageTabs", () => {
  it("renders one trigger per tab and forwards onChange", () => {
    const tabs = [
      { value: "a", label: "Alpha" },
      { value: "b", label: "Beta" },
    ];

    render(
      <MemoryRouter>
        <PageTabs tabs={tabs} value="a" onChange={() => {}}>
          <div data-testid="tab-content">content</div>
        </PageTabs>
      </MemoryRouter>,
    );

    expect(screen.getByRole("tab", { name: "Alpha" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Beta" })).toBeInTheDocument();
    expect(screen.getByTestId("tab-content")).toBeInTheDocument();
  });
});
