/**
 * API Error code constants - mirrors backend ErrorResponses.cs and ProblemDetailsHelper.cs
 *
 * These MUST stay in sync with backend constants.
 * Any changes to these values must be coordinated across FE/BE.
 */

/** Standard API error codes from ErrorResponses.cs */
export const ErrorCodes = {
  /** Resource not found (404) */
  NotFound: "NOT_FOUND",

  /** Validation error (400) */
  ValidationError: "VALIDATION_ERROR",

  /** Conflict error (409) */
  Conflict: "CONFLICT",
} as const;

/** Authentication/authorization error codes from ProblemDetailsHelper.AuthCodes */
export const AuthErrorCodes = {
  /** Keycloak identity is not linked to any user */
  IdentityNotLinked: "identity_not_linked",

  /** User exists but was not invited/activated */
  NotInvited: "not_invited",

  /** Email address not verified by identity provider */
  EmailNotVerified: "email_not_verified",

  /** User account is disabled or inactive */
  AccountInactive: "account_inactive",

  /** Invalid or missing authentication token */
  InvalidToken: "invalid_token",
} as const;

