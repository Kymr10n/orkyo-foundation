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
  siteRequests: (siteId: string) => `/api/sites/${siteId}/requests`,
  space: (siteId: string, resourceId: string) => `/api/sites/${siteId}/spaces/${resourceId}`,
  spaceCapabilities: (siteId: string, resourceId: string) => `/api/sites/${siteId}/spaces/${resourceId}/capabilities`,
  spaceCapability: (siteId: string, resourceId: string, capabilityId: string) =>
    `/api/sites/${siteId}/spaces/${resourceId}/capabilities/${capabilityId}`,

  // Groups
  GROUPS: '/api/groups',
  group: (groupId: string) => `/api/groups/${groupId}`,
  groupCapabilities: (groupId: string) => `/api/groups/${groupId}/capabilities`,
  groupCapability: (groupId: string, capabilityId: string) =>
    `/api/groups/${groupId}/capabilities/${capabilityId}`,

  // Requests
  REQUESTS: '/api/requests',
  CONFLICTS: '/api/conflicts',
  request: (requestId: string) => `/api/requests/${requestId}`,
  requestSchedule: (requestId: string) => `/api/requests/${requestId}/schedule`,
  requestRequirements: (requestId: string) => `/api/requests/${requestId}/requirements`,
  requestRequirement: (requestId: string, requirementId: string) =>
    `/api/requests/${requestId}/requirements/${requirementId}`,
  requestChildren: (requestId: string) => `/api/requests/${requestId}/children`,
  requestMove: (requestId: string) => `/api/requests/${requestId}/move`,
  requestSubtree: (requestId: string) => `/api/requests/${requestId}/subtree`,
  requestDescendantsCount: (requestId: string) => `/api/requests/${requestId}/descendants/count`,

  // Resource Types
  RESOURCE_TYPES: '/api/resource-types',

  // Resources
  RESOURCES: '/api/resources',
  resource: (resourceId: string) => `/api/resources/${resourceId}`,
  resourceUtilization: (resourceId: string) => `/api/resources/${resourceId}/utilization`,
  UTILIZATION_BY_RESOURCE: '/api/utilization/by-resource',
  resourceCandidateRequests: (resourceId: string) => `/api/resources/${resourceId}/candidate-requests`,
  resourceAssignments: (resourceId: string) => `/api/resources/${resourceId}/assignments`,
  resourceCapabilities: (resourceId: string) => `/api/resources/${resourceId}/capabilities`,
  resourceCapability: (resourceId: string, capabilityId: string) =>
    `/api/resources/${resourceId}/capabilities/${capabilityId}`,

  // Person Profiles
  PERSON_PROFILES: '/api/person-profiles',
  PERSON_PROFILE_JOB_TITLES: '/api/person-profiles/job-titles',
  PERSON_PROFILES_BATCH: '/api/person-profiles/batch',
  personProfile: (resourceId: string) => `/api/person-profiles/${resourceId}`,
  personProfileLink: (resourceId: string) => `/api/person-profiles/${resourceId}/link`,

  // Job Titles
  JOB_TITLES: '/api/job-titles',
  jobTitle: (id: string) => `/api/job-titles/${id}`,

  // Departments
  DEPARTMENTS: '/api/departments',
  DEPARTMENTS_TREE: '/api/departments/tree',
  department: (id: string) => `/api/departments/${id}`,

  // Templates
  TEMPLATES: '/api/templates',
  templatesWithType: (entityType: string) => `/api/templates?entityType=${entityType}`,
  template: (templateId: string) => `/api/templates/${templateId}`,

  // Criteria
  CRITERIA: '/api/criteria',
  criterion: (criterionId: string) => `/api/criteria/${criterionId}`,
  criterionApplicability: (criterionId: string) => `/api/criteria/${criterionId}/applicability`,

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

  // Account Security (Keycloak Admin API backed)
  ACCOUNT: {
    PASSWORD: '/api/account/password',
    SESSIONS: '/api/account/sessions',
    LOGOUT_ALL: '/api/account/logout-all',
    SECURITY_INFO: '/api/account/security-info',
    MFA_STATUS: '/api/account/mfa-status',
    MFA: '/api/account/mfa',
    PROFILE: '/api/account/profile',
    EMAIL: '/api/account/email',
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
    // Quotas
    SUBSCRIPTION_TIERS: '/api/admin/subscription-tiers',
    TENANTS_USAGE: '/api/admin/tenants/usage',
    tenantQuotas: (tenantId: string) => `/api/admin/tenants/${tenantId}/quotas`,
    tenantQuotaOverride: (tenantId: string, quotaKey: string) =>
      `/api/admin/tenants/${tenantId}/quota-overrides/${quotaKey}`,
    subscriptionTierQuota: (tierId: string, quotaKey: string) =>
      `/api/admin/subscription-tiers/${tierId}/quotas/${quotaKey}`,
  },

  // Scheduling
  scheduling: (siteId: string) => `/api/sites/${siteId}/scheduling`,

  // Auto-Schedule (Professional+ tier)
  AUTO_SCHEDULE_PREVIEW: '/api/scheduling/auto-schedule/preview',
  AUTO_SCHEDULE_APPLY: '/api/scheduling/auto-schedule/apply',

  // Tenant Settings (admin-configurable)
  SETTINGS: '/api/settings',
  setting: (key: string) => `/api/settings/${key}`,

  // Quota & Usage (tenant read-only)
  QUOTAS: '/api/settings/quotas',

  // Resource Groups (typed, e.g. people groups)
  RESOURCE_GROUPS: '/api/resource-groups',
  resourceGroup: (id: string) => `/api/resource-groups/${id}`,
  resourceGroupMembers: (id: string) => `/api/resource-groups/${id}/members`,

  // Resource Absences
  resourceAbsences: (resourceId: string) => `/api/resources/${resourceId}/absences`,
  resourceAbsence: (resourceId: string, absenceId: string) => `/api/resources/${resourceId}/absences/${absenceId}`,

  // Availability Events
  availabilityEvents: (siteId: string) => `/api/sites/${siteId}/availability-events`,
  availabilityEvent: (siteId: string, eventId: string) => `/api/sites/${siteId}/availability-events/${eventId}`,
  availabilityEventScopes: (siteId: string, eventId: string) => `/api/sites/${siteId}/availability-events/${eventId}/scopes`,
  availabilityEventScope: (siteId: string, eventId: string, scopeId: string) => `/api/sites/${siteId}/availability-events/${eventId}/scopes/${scopeId}`,

  // Resource Assignments
  RESOURCE_ASSIGNMENTS: '/api/resource-assignments',
  RESOURCE_ASSIGNMENTS_VALIDATE: '/api/resource-assignments/validate',
  RESOURCE_ASSIGNMENTS_VALIDATE_BATCH: '/api/resource-assignments/validate-batch',
  resourceAssignment: (id: string) => `/api/resource-assignments/${id}`,
} as const;
