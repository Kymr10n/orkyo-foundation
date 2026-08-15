/**
 * Space-related types for geometry-based space creation
 */

import type { CustomFieldValue } from '@foundation/src/lib/api/resource-custom-fields-api';

export interface Coordinate {
  x: number;
  y: number;
}

export type GeometryType = "rectangle" | "polygon";

export interface SpaceGeometry {
  type: GeometryType;
  coordinates: Coordinate[];
}

export interface Space {
  id: string;
  siteId: string;
  name: string;
  code?: string;
  description?: string;
  isPhysical: boolean;
  geometry?: SpaceGeometry;
  properties?: Record<string, unknown>;
  groupId?: string;
  capacity: number;
  /** Values for the space type's custom fields, keyed by field key. */
  customFields?: Record<string, CustomFieldValue>;
  createdAt: string;
  updatedAt: string;
}

export interface CreateSpaceRequest {
  name: string;
  code?: string;
  description?: string;
  isPhysical: boolean;
  geometry?: SpaceGeometry;
  properties?: Record<string, unknown>;
  capacity?: number;
  customFields?: Record<string, CustomFieldValue>;
}

export interface UpdateSpaceRequest {
  name?: string;
  code?: string;
  description?: string;
  isPhysical?: boolean;
  geometry?: SpaceGeometry;
  properties?: Record<string, unknown>;
  capacity?: number;
  /** The complete document — an omitted field is an emptied one, as on any resource. */
  customFields?: Record<string, CustomFieldValue>;
}

/**
 * Drawing state for interactive space creation
 */
export type DrawingMode =
  | "none"
  | "rectangle"
  | "polygon";
