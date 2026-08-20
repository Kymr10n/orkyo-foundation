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

export type GeometryType = 'rectangle' | 'polygon' | 'circle';

/**
 * What the coordinates mean, per type — the only place this contract is written down:
 *
 * - `rectangle` — exactly two points, opposite corners.
 * - `polygon` — three or more points, the outline in order.
 * - `circle` — exactly two points: the centre, then any point on the rim. The radius is the
 *   distance between them, so a resize moves the second point and leaves the first alone.
 *
 * Only the circle's points are not all on the outline, which is why anything deriving an extent
 * has to special-case it.
 */
export interface ResourceGeometry {
  type: GeometryType;
  coordinates: Coordinate[];
}

/** What the floorplan canvas is currently letting the user draw. */
export type DrawingMode = 'none' | 'rectangle' | 'polygon' | 'circle';
