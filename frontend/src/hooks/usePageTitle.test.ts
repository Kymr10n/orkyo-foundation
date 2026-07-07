import { describe, it, expect, afterEach } from "vitest";
import { renderHook } from "@testing-library/react";
import { usePageTitle } from "./usePageTitle";

describe("usePageTitle", () => {
  afterEach(() => {
    document.title = "Orkyo";
  });

  it("appends the Orkyo suffix on mount", () => {
    renderHook(() => usePageTitle("Requests"));
    expect(document.title).toBe("Requests — Orkyo");
  });

  it("updates the title when it changes", () => {
    const { rerender } = renderHook(({ t }) => usePageTitle(t), {
      initialProps: { t: "Spaces" },
    });
    expect(document.title).toBe("Spaces — Orkyo");
    rerender({ t: "People" });
    expect(document.title).toBe("People — Orkyo");
  });

  it("restores the base title on unmount", () => {
    const { unmount } = renderHook(() => usePageTitle("Settings"));
    expect(document.title).toBe("Settings — Orkyo");
    unmount();
    expect(document.title).toBe("Orkyo");
  });
});
