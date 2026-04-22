/**
 * Tracking & Analytics Type Definitions
 *
 * This module defines types for future tracking/analytics implementation.
 * Currently a placeholder to establish architectural boundaries.
 */

/**
 * Consent state for tracking/analytics
 * Future implementation will manage user consent for different tracking categories
 */
export enum ConsentState {
  /** User has not been asked for consent */
  Unknown = "unknown",
  /** User has explicitly accepted tracking */
  Accepted = "accepted",
  /** User has explicitly rejected tracking */
  Rejected = "rejected",
}

/**
 * Tracking event categories
 * Define event types that may be tracked in the future
 */
export enum TrackingCategory {
  /** Technically necessary events (always allowed) */
  Essential = "essential",
  /** Analytics events (require consent) */
  Analytics = "analytics",
  /** Marketing events (require consent) */
  Marketing = "marketing",
}

/**
 * Tracking provider configuration
 * Future implementation will use this to configure analytics providers
 */
export interface TrackingConfig {
  /** Whether tracking is enabled */
  enabled: boolean;
  /** Analytics provider configuration (e.g., Google Analytics ID) */
  providers?: {
    googleAnalytics?: {
      measurementId: string;
    };
    // Additional providers can be added here
  };
}
