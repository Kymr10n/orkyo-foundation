import { apiPost } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import type {
  Routing,
  CreateRoutingRequest,
  UpdateRoutingRequest,
  InstantiateRoutingRequest,
  InstantiateRoutingResponse,
} from '@foundation/src/types/routings';
import { createCrudApi } from './create-crud-api';

const routingsApi = createCrudApi<Routing, CreateRoutingRequest, UpdateRoutingRequest>({
  collectionPath: API_PATHS.ROUTINGS,
  itemPath: API_PATHS.routing,
});

export function getRoutings(): Promise<Routing[]> {
  return routingsApi.list();
}

export function createRouting(request: CreateRoutingRequest): Promise<Routing> {
  return routingsApi.create(request);
}

export function updateRouting(id: string, request: UpdateRoutingRequest): Promise<Routing> {
  return routingsApi.update(id, request);
}

export function deleteRouting(id: string): Promise<void> {
  return routingsApi.remove(id);
}

/** Creates a work order: a container with one leaf per step, chained finish-to-start. */
export function instantiateRouting(
  id: string,
  request: InstantiateRoutingRequest,
): Promise<InstantiateRoutingResponse> {
  return apiPost<InstantiateRoutingResponse>(API_PATHS.routingInstantiate(id), request);
}
