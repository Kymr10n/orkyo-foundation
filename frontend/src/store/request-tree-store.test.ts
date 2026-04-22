import { describe, it, expect, beforeEach } from "vitest";
import { useRequestTreeStore } from "./request-tree-store";

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
