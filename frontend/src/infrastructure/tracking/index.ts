/**
 * Tracking Infrastructure Module
 *
 * Centralized export point for all tracking-related functionality.
 *
 * ARCHITECTURAL CONSTRAINT:
 * All analytics and tracking integrations must be implemented through this module.
 * Direct third-party script injection in index.html or component files is prohibited.
 *
 * CURRENT STATE:
 * Placeholder implementation with no active tracking.
 * Establishes architectural boundaries for future GDPR-compliant implementation.
 */

export { ConsentState, TrackingCategory } from "./tracking.types";
export type { TrackingConfig } from "./tracking.types";
export { TrackingProvider, useTracking } from "./TrackingProvider";
