/** One operation of a routing: a request template plus this part's times on it. */
export interface RoutingStep {
  id: string;
  /** 1-based; steps run in this order. */
  stepNo: number;
  operationTemplateId: string;
  operationName: string;
  setupMinutes: number;
  runMinutesPerUnit: number;
  /** Minimum gap between this step's finish and the next step's start. */
  lagMinutesAfter: number;
}

/** A part's sequence of operations, instantiated per work order. */
export interface Routing {
  id: string;
  name: string;
  description?: string;
  steps: RoutingStep[];
  createdAt?: string;
  updatedAt?: string;
}

export interface RoutingStepRequest {
  stepNo: number;
  operationTemplateId: string;
  setupMinutes: number;
  runMinutesPerUnit: number;
  lagMinutesAfter: number;
}

export interface CreateRoutingRequest {
  name: string;
  description?: string;
  steps: RoutingStepRequest[];
}

export type UpdateRoutingRequest = CreateRoutingRequest;

export interface InstantiateRoutingRequest {
  name: string;
  siteId?: string;
  quantity: number;
  earliestStartTs?: string;
  latestEndTs?: string;
  parentRequestId?: string;
}

export interface InstantiateRoutingResponse {
  parent: { id: string; name: string };
  childIds: string[];
}
