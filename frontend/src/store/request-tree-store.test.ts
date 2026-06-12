import { describe, it, expect, beforeEach, vi } from "vitest";
import { useRequestTreeStore } from "./request-tree-store";

describe("request-tree-store hydration", () => {
  beforeEach(() => {
    vi.resetModules();
    localStorage.clear();
  });

  it("restores persisted expanded ids on init", async () => {
    localStorage.setItem("requestTree.expandedIds", JSON.stringify(["x", "y"]));
    const mod = await import("./request-tree-store");
    expect(mod.useRequestTreeStore.getState().expandedIds.has("x")).toBe(true);
  });

  it("falls back to an empty set on malformed JSON", async () => {
    localStorage.setItem("requestTree.expandedIds", "{not json");
    const mod = await import("./request-tree-store");
    expect(mod.useRequestTreeStore.getState().expandedIds.size).toBe(0);
  });

  it("ignores a non-array payload", async () => {
    localStorage.setItem("requestTree.expandedIds", JSON.stringify({ a: 1 }));
    const mod = await import("./request-tree-store");
    expect(mod.useRequestTreeStore.getState().expandedIds.size).toBe(0);
  });
});

describe("useRequestTreeStore", () => {
  beforeEach(() => {
    useRequestTreeStore.setState({
      expandedIds: new Set<string>(),
      selectedId: null,
    });
  });

  it("toggle adds and removes ids", () => {
    const { toggle } = useRequestTreeStore.getState();
    toggle("a");
    expect(useRequestTreeStore.getState().expandedIds.has("a")).toBe(true);
    toggle("a");
    expect(useRequestTreeStore.getState().expandedIds.has("a")).toBe(false);
  });

  it("expand is idempotent", () => {
    const { expand } = useRequestTreeStore.getState();
    expand("a");
    expand("a");
    expect(useRequestTreeStore.getState().expandedIds.size).toBe(1);
  });

  it("collapse removes an expanded id and no-ops when absent", () => {
    const { expand, collapse } = useRequestTreeStore.getState();
    expand("a");
    collapse("a");
    expect(useRequestTreeStore.getState().expandedIds.has("a")).toBe(false);
    // Collapsing an id that isn't expanded leaves the set unchanged.
    const before = useRequestTreeStore.getState().expandedIds;
    collapse("missing");
    expect(useRequestTreeStore.getState().expandedIds).toBe(before);
  });

  it("toggle persists the expanded ids to localStorage", () => {
    useRequestTreeStore.getState().toggle("a");
    expect(
      JSON.parse(localStorage.getItem("requestTree.expandedIds") ?? "[]"),
    ).toContain("a");
  });

  it("expandAll replaces the set", () => {
    const { expandAll } = useRequestTreeStore.getState();
    expandAll(["a", "b", "c"]);
    expect(useRequestTreeStore.getState().expandedIds.size).toBe(3);
  });

  it("collapseAll clears", () => {
    const { expandAll, collapseAll } = useRequestTreeStore.getState();
    expandAll(["a", "b"]);
    collapseAll();
    expect(useRequestTreeStore.getState().expandedIds.size).toBe(0);
  });

  it("expandAncestors merges into existing", () => {
    const { expand, expandAncestors } = useRequestTreeStore.getState();
    expand("a");
    expandAncestors(["b", "c"]);
    const ids = useRequestTreeStore.getState().expandedIds;
    expect(ids.has("a")).toBe(true);
    expect(ids.has("b")).toBe(true);
    expect(ids.has("c")).toBe(true);
  });

  it("setSelectedId updates selection", () => {
    useRequestTreeStore.getState().setSelectedId("x");
    expect(useRequestTreeStore.getState().selectedId).toBe("x");
    useRequestTreeStore.getState().setSelectedId(null);
    expect(useRequestTreeStore.getState().selectedId).toBeNull();
  });
});
