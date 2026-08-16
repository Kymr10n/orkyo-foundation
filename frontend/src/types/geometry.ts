/**
 * Geometry for placeable resources — anything whose type declares `hasGeometry`, not spaces
 * alone. These are wire-format shapes plus the canvas's drawing state, so they carry no resource
 * identity and are shared by every surface that draws on a floorplan.
 */

/** A point on the floorplan, in absolute image pixels. */
export interface Coordinate {
  x: number;
  y: number;
}

export type GeometryType = 'rectangle' | 'polygon';

/** A rectangle is exactly two points (top-left, bottom-right); a polygon is three or more. */
export interface ResourceGeometry {
  type: GeometryType;
  coordinates: Coordinate[];
}

/** What the floorplan canvas is currently letting the user draw. */
export type DrawingMode = 'none' | 'rectangle' | 'polygon';
