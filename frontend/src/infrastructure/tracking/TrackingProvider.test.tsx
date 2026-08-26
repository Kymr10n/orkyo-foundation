/**
 * Tests for TrackingProvider component
 *
 * These tests verify the placeholder implementation and ensure
 * the tracking layer is properly structured for future enhancement.
 */

import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { TrackingProvider } from "./TrackingProvider";

describe("TrackingProvider", () => {
  it("renders children without modification", () => {
    const { container } = render(
      <TrackingProvider>
        <div data-testid="test-child">Test Content</div>
      </TrackingProvider>,
    );

    expect(container.querySelector('[data-testid="test-child"]')).toBeTruthy();
    expect(container.textContent).toBe("Test Content");
  });

  it("accepts optional config prop", () => {
    const config = {
      enabled: false,
      providers: {
        googleAnalytics: {
          measurementId: "G-XXXXXXXXXX",
        },
      },
    };

    const { container } = render(
      <TrackingProvider config={config}>
        <div>Content</div>
      </TrackingProvider>,
    );

    expect(container.textContent).toBe("Content");
  });

  it("renders nested children correctly", () => {
    const { container } = render(
      <TrackingProvider>
        <div>
          <span>Nested</span>
          <span>Content</span>
        </div>
      </TrackingProvider>,
    );

    expect(container.querySelectorAll("span")).toHaveLength(2);
  });
});
