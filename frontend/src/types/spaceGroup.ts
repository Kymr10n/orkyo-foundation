export interface SpaceGroup {
  id: string;
  name: string;
  description?: string;
  color?: string; // Hex color (#RRGGBB)
  displayOrder: number;
  createdAt: string;
  updatedAt: string;
  spaceCount?: number;
}

export interface CreateSpaceGroupRequest {
  name: string;
  description?: string;
  color?: string;
  displayOrder?: number;
}

export interface UpdateSpaceGroupRequest {
  name?: string;
  description?: string;
  color?: string;
  displayOrder?: number;
}
