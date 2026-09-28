import { describe, it, expect, beforeEach, vi } from "vitest";
import { useRequestTreeStore } from "./request-tree-store";

const KEY = "orkyo.requestTree";
const persisted = (state: unknown) => localStorage.setItem(KEY, JSON.stringify({ state, version: 0 }));
const stored = () => JSON.parse(localStorage.getItem(KEY) ?? "{}").state;

describe("request-tree-store hydration", () => {
  beforeEach(() => {
    vi.resetModules();
    localStorage.clear();
  });

  it("restores persisted expanded ids and view mode on init", async () => {
    persisted({ expandedIds: ["x", "y"], viewMode: "list" });
    const mod = await import("./request-tree-store");
    const state = mod.useRequestTreeStore.getState();
    expect(state.expandedIds.has("x")).toBe(true);
    expect(state.viewMode).toBe("list");
  });

  it("starts empty on malformed JSON", async () => {
    localStorage.setItem(KEY, "{not json");
    const mod = await import("./request-tree-store");
    expect(mod.useRequestTreeStore.getState().expandedIds.size).toBe(0);
  });

  it("ignores a non-array payload and non-string ids", async () => {
    persisted({ expandedIds: { a: 1 } });
    let mod = await import("./request-tree-store");
    expect(mod.useRequestTreeStore.getState().expandedIds.size).toBe(0);

    vi.resetModules();
    persisted({ expandedIds: ["a", 7] });
    mod = await import("./request-tree-store");
    expect([...mod.useRequestTreeStore.getState().expandedIds]).toEqual(["a"]);
  });

  it("defaults viewMode to tree when nothing is persisted", async () => {
    const mod = await import("./request-tree-store");
    expect(mod.useRequestTreeStore.getState().viewMode).toBe("tree");
  });

  it("falls back to tree for an unrecognized persisted viewMode", async () => {
    persisted({ expandedIds: [], viewMode: "grid" });
    const mod = await import("./request-tree-store");
    expect(mod.useRequestTreeStore.getState().viewMode).toBe("tree");
  });
});

describe("useRequestTreeStore", () => {
  beforeEach(() => {
    useRequestTreeStore.setState({
      expandedIds: new Set<string>(),
      selectedId: null,
      viewMode: "tree",
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
    expect(stored().expandedIds).toContain("a");
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

  it("does not persist the selection", () => {
    useRequestTreeStore.getState().setSelectedId("x");
    expect(stored().selectedId).toBeUndefined();
  });

  it("setSelectedId updates selection", () => {
    useRequestTreeStore.getState().setSelectedId("x");
    expect(useRequestTreeStore.getState().selectedId).toBe("x");
    useRequestTreeStore.getState().setSelectedId(null);
    expect(useRequestTreeStore.getState().selectedId).toBeNull();
  });

  it("setViewMode updates state and persists to localStorage", () => {
    useRequestTreeStore.getState().setViewMode("list");
    expect(useRequestTreeStore.getState().viewMode).toBe("list");
    expect(stored().viewMode).toBe("list");

    useRequestTreeStore.getState().setViewMode("tree");
    expect(useRequestTreeStore.getState().viewMode).toBe("tree");
    expect(stored().viewMode).toBe("tree");
  });
});
