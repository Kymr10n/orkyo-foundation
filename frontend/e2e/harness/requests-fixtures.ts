import { makeRequest, spaceAssignment } from "@foundation/src/test-utils/request-fixtures";
import type { Request } from "@foundation/src/types/requests";
import type { ResourceInfo } from "@foundation/src/lib/api/resources-api";
import type { ResourceAssignmentInfo } from "@foundation/src/lib/api/resource-assignments-api";
import type { Space } from "@foundation/src/types/space";
import type { Site } from "@foundation/src/types/site";

/**
 * Fixture data for the Requests harness section (tree/list).
 * Shape: one expanded top-level group (with a nested subgroup among its
 * direct children) and one collapsed top-level group, so both the expanded
 * children rows and the collapsed child-count badge render.
 *
 *   grp-conf (summary, expanded)
 *     ├─ req-avset       leaf, new
 *     ├─ req-catering    leaf, in_progress, 3 conflicts
 *     ├─ req-signage     leaf, done
 *     └─ grp-loadin (container, collapsed — nested subgroup)
 *          ├─ req-loadin-labor  leaf, new
 *          └─ req-loadin-truck  leaf, in_progress
 *   grp-vendor (summary, collapsed)
 *     ├─ req-vendor-linens  leaf, new
 *     ├─ req-vendor-flowers leaf, in_progress
 *     └─ req-vendor-av      leaf, done
 */

export const GROUP_A_ID = "grp-conf";
export const GROUP_B_ID = "grp-vendor";
export const NESTED_GROUP_ID = "grp-loadin";
export const CONFLICT_REQUEST_ID = "req-catering";

export const requestFixtures: Request[] = [
  makeRequest({
    id: GROUP_A_ID,
    name: "Conference Setup",
    planningMode: "summary",
    status: "in_progress",
  }),
  makeRequest({
    id: "req-avset",
    name: "AV Setup",
    parentRequestId: GROUP_A_ID,
    planningMode: "leaf",
    status: "new",
    startTs: "2026-04-20T08:00:00Z",
    endTs: "2026-04-20T10:00:00Z",
    minimalDurationValue: 2,
    minimalDurationUnit: "hours",
  }),
  makeRequest({
    id: CONFLICT_REQUEST_ID,
    name: "Catering Delivery",
    parentRequestId: GROUP_A_ID,
    planningMode: "leaf",
    status: "in_progress",
    startTs: "2026-04-20T09:00:00Z",
    endTs: "2026-04-20T11:30:00Z",
    minimalDurationValue: 150,
    minimalDurationUnit: "minutes",
  }),
  makeRequest({
    id: "req-signage",
    name: "Signage Install",
    parentRequestId: GROUP_A_ID,
    planningMode: "leaf",
    status: "done",
    startTs: "2026-04-19T13:00:00Z",
    endTs: "2026-04-19T15:00:00Z",
    minimalDurationValue: 2,
    minimalDurationUnit: "hours",
  }),
  makeRequest({
    id: NESTED_GROUP_ID,
    name: "Vendor Load-in",
    parentRequestId: GROUP_A_ID,
    planningMode: "container",
    status: "new",
    earliestStartTs: "2026-04-20T05:00:00Z",
    latestEndTs: "2026-04-20T09:00:00Z",
  }),
  makeRequest({
    id: "req-loadin-labor",
    name: "Load-in Labor Crew",
    parentRequestId: NESTED_GROUP_ID,
    planningMode: "leaf",
    status: "new",
    startTs: "2026-04-20T05:00:00Z",
    endTs: "2026-04-20T07:00:00Z",
    minimalDurationValue: 2,
    minimalDurationUnit: "hours",
  }),
  makeRequest({
    id: "req-loadin-truck",
    name: "Truck Unload",
    parentRequestId: NESTED_GROUP_ID,
    planningMode: "leaf",
    status: "in_progress",
    startTs: "2026-04-20T07:00:00Z",
    endTs: "2026-04-20T09:00:00Z",
    minimalDurationValue: 2,
    minimalDurationUnit: "hours",
  }),
  makeRequest({
    id: GROUP_B_ID,
    name: "Vendor Coordination",
    planningMode: "summary",
    status: "new",
  }),
  makeRequest({
    id: "req-vendor-linens",
    name: "Linens Rental",
    parentRequestId: GROUP_B_ID,
    planningMode: "leaf",
    status: "new",
    startTs: "2026-04-21T08:00:00Z",
    endTs: "2026-04-21T09:00:00Z",
    minimalDurationValue: 1,
    minimalDurationUnit: "hours",
  }),
  makeRequest({
    id: "req-vendor-flowers",
    name: "Floral Delivery",
    parentRequestId: GROUP_B_ID,
    planningMode: "leaf",
    status: "in_progress",
    startTs: "2026-04-21T09:00:00Z",
    endTs: "2026-04-21T10:00:00Z",
    minimalDurationValue: 1,
    minimalDurationUnit: "hours",
  }),
  makeRequest({
    id: "req-vendor-av",
    name: "Extra AV Rental",
    parentRequestId: GROUP_B_ID,
    planningMode: "leaf",
    status: "done",
    startTs: "2026-04-21T07:00:00Z",
    endTs: "2026-04-21T08:00:00Z",
    minimalDurationValue: 1,
    minimalDurationUnit: "hours",
  }),
];

/** Feeds the tree's red AlertTriangle conflict pill (see RequestTreeView). */
export const conflictMapFixture = new Map<string, number>([[CONFLICT_REQUEST_ID, 3]]);

/*
 * Fixtures for the RequestFormDialog visual review (e2e/requests-dialog-visual.spec.ts).
 * Backed by the harness-only API stubs in e2e/harness/api-stubs/ (wired via vite
 * aliases in vite.config.ts) so the dialog's reference-data queries (spaces,
 * sites, people, assignments, children) resolve without a backend.
 */

export const SITE_ID = "site-1";
export const SPACE_BAY3_ID = "space-bay3";
export const PERSON_ALEX_ID = "person-alex";
export const PERSON_SAM_ID = "person-sam";

export const sitesFixture: Site[] = [
  {
    id: SITE_ID,
    code: "main",
    name: "Main Site",
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
  },
];

export const spacesFixture: Space[] = [
  {
    id: SPACE_BAY3_ID,
    siteId: SITE_ID,
    name: "Bay 3",
    isPhysical: true,
    capacity: 1,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
  },
];

export const peopleFixture: ResourceInfo[] = [
  {
    id: PERSON_ALEX_ID,
    resourceTypeId: "rt-person",
    resourceTypeKey: "person",
    name: "Alex Welder",
    allocationMode: "Shared",
    baseAvailabilityPercent: 100,
    isActive: true,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
  },
  {
    id: PERSON_SAM_ID,
    resourceTypeId: "rt-person",
    resourceTypeKey: "person",
    name: "Sam Fabricator",
    allocationMode: "Shared",
    baseAvailabilityPercent: 100,
    isActive: true,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
  },
];

/** LEAF request rendered in VIEW mode — Resources tab must resolve these IDs to names. */
export const LEAF_VIEW_REQUEST_ID = "req-leaf-view";

export const leafViewRequestFixture: Request = makeRequest({
  id: LEAF_VIEW_REQUEST_ID,
  name: "Stage Rigging",
  planningMode: "leaf",
  status: "in_progress",
  siteId: SITE_ID,
  startTs: "2026-04-22T08:00:00Z",
  endTs: "2026-04-22T10:00:00Z",
  minimalDurationValue: 2,
  minimalDurationUnit: "hours",
  assignments: [spaceAssignment(SPACE_BAY3_ID, { startUtc: "2026-04-22T08:00:00Z", endUtc: "2026-04-22T10:00:00Z" })],
});

/** Assignments returned by the assignments API stub for the leaf-view request above. */
export const leafViewAssignmentsFixture: ResourceAssignmentInfo[] = [
  {
    id: "assign-alex",
    requestId: LEAF_VIEW_REQUEST_ID,
    resourceId: PERSON_ALEX_ID,
    resourceTypeKey: "person",
    startUtc: "2026-04-22T08:00:00Z",
    endUtc: "2026-04-22T10:00:00Z",
    allocationPercent: 100,
    assignmentStatus: "Planned",
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
  },
  {
    id: "assign-sam",
    requestId: LEAF_VIEW_REQUEST_ID,
    resourceId: PERSON_SAM_ID,
    resourceTypeKey: "person",
    startUtc: "2026-04-22T08:00:00Z",
    endUtc: "2026-04-22T10:00:00Z",
    allocationPercent: 100,
    assignmentStatus: "Planned",
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
  },
];

/** GROUP request rendered in EDIT mode — Children tab must list its 2 children below. */
export const GROUP_EDIT_REQUEST_ID = "req-group-edit";

const groupEditChild1: Request = makeRequest({
  id: "req-group-edit-child-1",
  name: "Rig Truss",
  parentRequestId: GROUP_EDIT_REQUEST_ID,
  planningMode: "leaf",
  status: "new",
  startTs: "2026-04-22T08:00:00Z",
  endTs: "2026-04-22T09:00:00Z",
  minimalDurationValue: 1,
  minimalDurationUnit: "hours",
});

const groupEditChild2: Request = makeRequest({
  id: "req-group-edit-child-2",
  name: "Sound Check",
  parentRequestId: GROUP_EDIT_REQUEST_ID,
  planningMode: "leaf",
  status: "new",
  startTs: "2026-04-22T09:00:00Z",
  endTs: "2026-04-22T10:00:00Z",
  minimalDurationValue: 1,
  minimalDurationUnit: "hours",
});

// Extra children beyond the first two — long enough a list that the visual
// review proves the quick-add row stays pinned above it, not just visible
// with a two-row list.
const groupEditChild3: Request = makeRequest({
  id: "req-group-edit-child-3",
  name: "Load Chairs",
  parentRequestId: GROUP_EDIT_REQUEST_ID,
  planningMode: "leaf",
  status: "new",
  startTs: "2026-04-22T10:00:00Z",
  endTs: "2026-04-22T11:00:00Z",
  minimalDurationValue: 1,
  minimalDurationUnit: "hours",
});

const groupEditChild4: Request = makeRequest({
  id: "req-group-edit-child-4",
  name: "Set Tables",
  parentRequestId: GROUP_EDIT_REQUEST_ID,
  planningMode: "leaf",
  status: "new",
  startTs: "2026-04-22T11:00:00Z",
  endTs: "2026-04-22T12:00:00Z",
  minimalDurationValue: 1,
  minimalDurationUnit: "hours",
});

const groupEditChild5: Request = makeRequest({
  id: "req-group-edit-child-5",
  name: "Test Mics",
  parentRequestId: GROUP_EDIT_REQUEST_ID,
  planningMode: "leaf",
  status: "new",
  startTs: "2026-04-22T12:00:00Z",
  endTs: "2026-04-22T13:00:00Z",
  minimalDurationValue: 1,
  minimalDurationUnit: "hours",
});

export const groupEditRequestFixture: Request = makeRequest({
  id: GROUP_EDIT_REQUEST_ID,
  name: "Concert Load-in",
  planningMode: "summary",
  status: "new",
});

// Parentless requests — candidates for the Children tab's "Add existing"
// picker (req-group-edit itself has no parent, so these must too, to prove
// the panel filters correctly and isn't just listing everything).
const looseTaskA: Request = makeRequest({
  id: "req-loose-task-a",
  name: "Loose Task A",
  planningMode: "leaf",
  status: "new",
  startTs: "2026-04-23T08:00:00Z",
  endTs: "2026-04-23T09:00:00Z",
  minimalDurationValue: 1,
  minimalDurationUnit: "hours",
});

const looseGroupB: Request = makeRequest({
  id: "req-loose-group-b",
  name: "Loose Group B",
  planningMode: "summary",
  status: "new",
});

/** Full tree passed as `allRequests` for the group-edit case (drives the Children tab). */
export const groupEditAllRequests: Request[] = [
  groupEditRequestFixture,
  groupEditChild1,
  groupEditChild2,
  groupEditChild3,
  groupEditChild4,
  groupEditChild5,
  looseTaskA,
  looseGroupB,
];

/** Keyed by request id — feeds the request-api stub's getRequestChildren(). */
export const requestChildrenFixture = new Map<string, Request[]>([
  [
    GROUP_EDIT_REQUEST_ID,
    [groupEditChild1, groupEditChild2, groupEditChild3, groupEditChild4, groupEditChild5],
  ],
]);
