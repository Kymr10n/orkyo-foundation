export interface User {
  id: string;
  email: string;
  displayName: string;
  role?: 'admin' | 'editor' | 'viewer' | 'inactive';
  status?: 'pending' | 'active' | 'suspended';
  createdAt?: string;
  lastLoginAt?: string;
  // Multi-tenant fields
  tenantSlug?: string;
  isTenantAdmin?: boolean;
}
