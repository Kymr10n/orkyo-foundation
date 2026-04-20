export interface Site {
  id: string;
  code: string;
  name: string;
  description?: string;
  address?: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateSiteRequest {
  code: string;
  name: string;
  description?: string;
  address?: string;
}

export interface UpdateSiteRequest {
  code: string;
  name: string;
  description?: string;
  address?: string;
}