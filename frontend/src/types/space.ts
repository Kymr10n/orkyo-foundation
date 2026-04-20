/**
 * Space-related types for geometry-based space creation
 */

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
}

export interface UpdateSpaceRequest {
  name?: string;
  code?: string;
  description?: string;
  isPhysical?: boolean;
  geometry?: SpaceGeometry;
  properties?: Record<string, unknown>;
  groupId?: string | null;
  capacity?: number;
}

/**
 * Drawing state for interactive space creation
 */
export type DrawingMode =
  | "none"
  | "rectangle"
  | "polygon"
  | "select"
  | "resize";

