import { describe, expect, it } from "vitest";
import type { FlatTreeEntry } from "@foundation/src/domain/request-tree";
import type { Request, RequestStatus } from "@foundation/src/types/requests";
import { filterPanelEntries } from "./request-panel-filter";

function req(id: string, status: RequestStatus, extra: Partial<Request> = {}): Request {
  return { id, name: id, status, planningMode: "leaf", isScheduled: false, ...extra } as Request;
}

function entry(request: Request, depth: number, hasChildren = false): FlatTreeEntry {
  return { request, depth, hasChildren, isLastChild: false };
}

// A collapsed summary parent with an in_progress child, plus a top-level in_progress and a top-level new.
const parent = entry(req("parent", "new", { planningMode: "container" }), 0, true);
const child = entry(req("child", "in_progress", { isScheduled: true }), 1);
const topInProgress = entry(req("top-ip", "in_progress", { isScheduled: true }), 0);
const topNew = entry(req("top-new", "new"), 0);
const entries = [parent, child, topInProgress, topNew];

const collapsedParent = new Set<string>(["parent"]);

describe("filterPanelEntries", () => {
  it("reveals an in_progress request nested under a COLLAPSED parent when filtering by status", () => {
    const ids = filterPanelEntries(entries, {
      searchQuery: "",
      statusFilter: "in_progress",
      scheduledFilter: "all",
      collapsedIds: collapsedParent,
    }).map((e) => e.request.id);

    // The nested child AND the top-level one both show — collapse is ignored while filtering.
    expect(ids).toEqual(["child", "top-ip"]);
  });

  it("still honours collapse when no status filter / search is active", () => {
    const ids = filterPanelEntries(entries, {
      searchQuery: "",
      statusFilter: "all",
      scheduledFilter: "all",
      collapsedIds: collapsedParent,
    }).map((e) => e.request.id);

    // The child stays hidden under its collapsed parent; top-level entries show.
    expect(ids).toEqual(["parent", "top-ip", "top-new"]);
  });

  it("reveals nested matches when searching, regardless of collapse", () => {
    const ids = filterPanelEntries(entries, {
      searchQuery: "child",
      statusFilter: "all",
      scheduledFilter: "all",
      collapsedIds: collapsedParent,
    }).map((e) => e.request.id);

    expect(ids).toEqual(["child"]);
  });

  it("parks 'deferred' under All Statuses but shows it when explicitly selected", () => {
    const withDeferred = [...entries, entry(req("def", "deferred"), 0)];
    const underAll = filterPanelEntries(withDeferred, {
      searchQuery: "", statusFilter: "all", scheduledFilter: "all", collapsedIds: new Set(),
    }).map((e) => e.request.id);
    expect(underAll).not.toContain("def");

    const underDeferred = filterPanelEntries(withDeferred, {
      searchQuery: "", statusFilter: "deferred", scheduledFilter: "all", collapsedIds: new Set(),
    }).map((e) => e.request.id);
    expect(underDeferred).toEqual(["def"]);
  });
});
