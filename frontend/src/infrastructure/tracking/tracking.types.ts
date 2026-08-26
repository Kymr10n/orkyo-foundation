/**
 * Tracking & Analytics Type Definitions
 *
 * This module defines types for future tracking/analytics implementation.
 * Currently a placeholder to establish architectural boundaries.
 */

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
