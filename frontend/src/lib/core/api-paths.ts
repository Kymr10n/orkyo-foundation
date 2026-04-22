/**
 * Centralized API path constants.
 * 
 * All API endpoints should be defined here to:
 * 1. Prevent typos and path mismatches
 * 2. Make refactoring easier (change once, update everywhere)
 * 3. Enable tests to verify the correct paths are used
 */

export const API_PATHS = {
  // Session (OIDC authenticated, no tenant context required)
  SESSION: {
    BOOTSTRAP: '/api/session/bootstrap',
    ME: '/api/session/me',
    TOUR_SEEN: '/api/session/tour/seen',
    TOS_ACCEPT: '/api/session/tos/accept',
  },

  // Search
  SEARCH: '/api/search',

  // Sites
  SITES: '/api/sites',
  site: (siteId: string) => `/api/sites/${siteId}`,
  siteFloorplan: (siteId: string) => `/api/sites/${siteId}/floorplan`,
  siteFloorplanMetadata: (siteId: string) => `/api/sites/${siteId}/floorplan/metadata`,

  // Spaces
  spaces: (siteId: string) => `/api/sites/${siteId}/spaces`,
  space: (siteId: string, spaceId: string) => `/api/sites/${siteId}/spaces/${spaceId}`,
  spaceCapabilities: (siteId: string, spaceId: string) => `/api/sites/${siteId}/spaces/${spaceId}/capabilities`,
  spaceCapability: (siteId: string, spaceId: string, capabilityId: string) =>
    `/api/sites/${siteId}/spaces/${spaceId}/capabilities/${capabilityId}`,

  // Groups
  GROUPS: '/api/groups',
  group: (groupId: string) => `/api/groups/${groupId}`,
  groupCapabilities: (groupId: string) => `/api/groups/${groupId}/capabilities`,
  groupCapability: (groupId: string, capabilityId: string) =>
    `/api/groups/${groupId}/capabilities/${capabilityId}`,

  // Requests
  REQUESTS: '/api/requests',
  request: (requestId: string) => `/api/requests/${requestId}`,
  requestSchedule: (requestId: string) => `/api/requests/${requestId}/schedule`,
  requestRequirements: (requestId: string) => `/api/requests/${requestId}/requirements`,
  requestRequirement: (requestId: string, requirementId: string) =>
    `/api/requests/${requestId}/requirements/${requirementId}`,
  requestChildren: (requestId: string) => `/api/requests/${requestId}/children`,
  requestMove: (requestId: string) => `/api/requests/${requestId}/move`,
  requestSubtree: (requestId: string) => `/api/requests/${requestId}/subtree`,
  requestDescendantsCount: (requestId: string) => `/api/requests/${requestId}/descendants/count`,

  // Templates
  TEMPLATES: '/api/templates',
  templatesWithType: (entityType: string) => `/api/templates?entityType=${entityType}`,
  template: (templateId: string) => `/api/templates/${templateId}`,

  // Criteria
  CRITERIA: '/api/criteria',
  criterion: (criterionId: string) => `/api/criteria/${criterionId}`,

  // Users
  USERS: '/api/users',
  USER_INVITATIONS: '/api/users/invitations',
  USER_INVITE: '/api/users/invite',
  INVITATION_VALIDATE: '/api/invitations/validate',
  INVITATION_ACCEPT: '/api/invitations/accept',
  user: (userId: string) => `/api/users/${userId}`,
  userRole: (userId: string) => `/api/users/${userId}/role`,
  userInvitation: (invitationId: string) => `/api/users/invitations/${invitationId}`,
  userInvitationResend: (invitationId: string) => `/api/users/invitations/${invitationId}/resend`,

  // Feedback
  FEEDBACK: '/api/feedback',

  // Tenant Management (no tenant context required)
  TENANTS: {
    CAN_CREATE: '/api/tenants/can-create',
    CREATE: '/api/tenants',
    STARTER_TEMPLATES: '/api/tenants/starter-templates',
    MEMBERSHIPS: '/api/tenants/memberships',
    byId: (tenantId: string) => `/api/tenants/${tenantId}`,
    leave: (tenantId: string) => `/api/tenants/${tenantId}/leave`,
    delete: (tenantId: string) => `/api/tenants/${tenantId}`,
    cancelDeletion: (tenantId: string) => `/api/tenants/${tenantId}/cancel-deletion`,
    transferOwnership: (tenantId: string) => `/api/tenants/${tenantId}/transfer-ownership`,
  },

  // Interest Registration (anonymous)
  INTEREST: '/api/interest',

  // Account Security (Keycloak Admin API backed)
  ACCOUNT: {
    PASSWORD: '/api/account/password',
    SESSIONS: '/api/account/sessions',
    LOGOUT_ALL: '/api/account/logout-all',
    SECURITY_INFO: '/api/account/security-info',
    MFA_STATUS: '/api/account/mfa-status',
    MFA: '/api/account/mfa',
    PROFILE: '/api/account/profile',
    session: (sessionId: string) => `/api/account/sessions/${sessionId}`,
  },

  // Preferences
  PREFERENCES: '/api/preferences',

  // Announcements
  ANNOUNCEMENTS: '/api/announcements',
  announcementRead: (id: string) => `/api/announcements/${id}/read`,
  ANNOUNCEMENTS_UNREAD_COUNT: '/api/announcements/unread-count',

  // Admin
  ADMIN: {
    TENANTS: '/api/admin/tenants',
    USERS: '/api/admin/users',
    BREAK_GLASS_ENTRY: '/api/admin/break-glass/entry',
    BREAK_GLASS_EXIT: '/api/admin/break-glass/exit',
    BREAK_GLASS_RENEW: '/api/admin/break-glass/renew',
    breakGlassSession: (tenantSlug: string) =>
      `/api/admin/break-glass/session/${encodeURIComponent(tenantSlug)}`,
    PRESETS_VALIDATE: '/api/admin/presets/validate',
    PRESETS_APPLY: '/api/admin/presets/apply',
    PRESETS_EXPORT: '/api/admin/presets/export',
    PRESETS_APPLICATIONS: '/api/admin/presets/applications',
    EXPORT: '/api/admin/export',
    SETTINGS: '/api/admin/settings',
    DIAGNOSTICS: '/api/admin/diagnostics',
    tenant: (tenantId: string) => `/api/admin/tenants/${tenantId}`,
    tenantTier: (tenantId: string) => `/api/admin/tenants/${tenantId}/tier`,
    tenantMembers: (tenantId: string) => `/api/admin/tenants/${tenantId}/members`,
    tenantMember: (tenantId: string, userId: string) => `/api/admin/tenants/${tenantId}/members/${userId}`,
    user: (userId: string) => `/api/admin/users/${userId}`,
    userMemberships: (userId: string) => `/api/admin/users/${userId}/memberships`,
    userDeactivate: (userId: string) => `/api/admin/users/${userId}/deactivate`,
    userReactivate: (userId: string) => `/api/admin/users/${userId}/reactivate`,
    userPromoteSiteAdmin: (userId: string) => `/api/admin/users/${userId}/promote-site-admin`,
    userRevokeSiteAdmin: (userId: string) => `/api/admin/users/${userId}/revoke-site-admin`,
  },

  // Scheduling
  scheduling: (siteId: string) => `/api/sites/${siteId}/scheduling`,
  offTimes: (siteId: string) => `/api/sites/${siteId}/off-times`,
  offTime: (siteId: string, offTimeId: string) => `/api/sites/${siteId}/off-times/${offTimeId}`,

  // Auto-Schedule (Professional+ tier)
  AUTO_SCHEDULE_PREVIEW: '/api/scheduling/auto-schedule/preview',
  AUTO_SCHEDULE_APPLY: '/api/scheduling/auto-schedule/apply',

  // Tenant Settings (admin-configurable)
  SETTINGS: '/api/settings',
  setting: (key: string) => `/api/settings/${key}`,
} as const;
