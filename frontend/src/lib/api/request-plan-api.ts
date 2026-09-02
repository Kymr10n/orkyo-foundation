/**
 * API client for a request's plan: its children, the dependencies among them, and whether each
 * child may start yet.
 *
 * One read rather than a children fetch plus an edge fetch per child — the planner draws the
 * whole graph at once, and a per-node round trip would make opening it O(children).
 */

import { apiGet } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";
import type { RequestDependency } from "./request-dependency-api";
import type { PlanningMode, PredecessorLogic, RequestStatus } from "../../types/requests";

export interface RequestPlanChild {
  id: string;
  name: string;
  planningMode: PlanningMode;
  /** Schedule-derived status, as everywhere else in the read model. */
  status: RequestStatus;
  startTs: string | null;
  endTs: string | null;
  sortOrder: number;
  icon: string | null;
  predecessorLogic: PredecessorLogic;
  predecessorLogicK: number | null;
  /** Whether the join condition is satisfied right now — the same answer the server's gate gives. */
  canStart: boolean;
  /** Edges to and from requests outside this group, which the planner has no node to draw. */
  externalPredecessorCount: number;
  externalSuccessorCount: number;
}

export interface RequestPlan {
  parentId: string;
  parentName: string;
  parentPlanningMode: PlanningMode;
  children: RequestPlanChild[];
  /** Only edges with BOTH ends among the children; the rest are counted on the child instead. */
  edges: RequestDependency[];
}

export function getRequestPlan(requestId: string): Promise<RequestPlan> {
  return apiGet<RequestPlan>(API_PATHS.requestPlan(requestId));
}
