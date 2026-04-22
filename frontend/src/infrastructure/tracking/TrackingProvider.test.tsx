/**
 * Tests for TrackingProvider component and useTracking hook
 *
 * These tests verify the placeholder implementation and ensure
 * the tracking layer is properly structured for future enhancement.
 */

import { render, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { TrackingProvider, useTracking } from "./TrackingProvider";
import { ConsentState, TrackingCategory } from "./tracking.types";

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

describe("useTracking", () => {
  it("returns tracking API methods", () => {
    const { result } = renderHook(() => useTracking());

    expect(result.current).toHaveProperty("trackEvent");
    expect(result.current).toHaveProperty("setConsent");
    expect(result.current).toHaveProperty("getConsent");
  });

  it("trackEvent is a function", () => {
    const { result } = renderHook(() => useTracking());

    expect(typeof result.current.trackEvent).toBe("function");
  });

  it("setConsent is a function", () => {
    const { result } = renderHook(() => useTracking());

    expect(typeof result.current.setConsent).toBe("function");
  });

  it("getConsent returns unknown by default", () => {
    const { result } = renderHook(() => useTracking());

    expect(result.current.getConsent()).toBe("unknown");
  });

  it("trackEvent is a no-op (does not throw)", () => {
    const { result } = renderHook(() => useTracking());

    expect(() => {
      result.current.trackEvent();
    }).not.toThrow();
  });

  it("setConsent is a no-op (does not throw)", () => {
    const { result } = renderHook(() => useTracking());

    expect(() => {
      result.current.setConsent();
    }).not.toThrow();
  });
});

describe("Tracking Types", () => {
  it("ConsentState enum has expected values", () => {
    expect(ConsentState.Unknown).toBe("unknown");
    expect(ConsentState.Accepted).toBe("accepted");
    expect(ConsentState.Rejected).toBe("rejected");
  });

  it("TrackingCategory enum has expected values", () => {
    expect(TrackingCategory.Essential).toBe("essential");
    expect(TrackingCategory.Analytics).toBe("analytics");
    expect(TrackingCategory.Marketing).toBe("marketing");
  });
});

describe("Integration", () => {
  it("useTracking works inside TrackingProvider", () => {
    const TestComponent = () => {
      const tracking = useTracking();
      return <div>{tracking.getConsent()}</div>;
    };

    const { container } = render(
      <TrackingProvider>
        <TestComponent />
      </TrackingProvider>,
    );

    expect(container.textContent).toBe("unknown");
  });

  it("useTracking works outside TrackingProvider (standalone)", () => {
    const { result } = renderHook(() => useTracking());

    // Should still work even without provider context
    expect(result.current.getConsent()).toBe("unknown");
  });
});
