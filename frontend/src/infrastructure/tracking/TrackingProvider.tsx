/**
 * TrackingProvider - Centralized Tracking & Analytics Layer
 *
 * ARCHITECTURAL RULE:
 * All analytics, marketing scripts, and third-party tracking integrations
 * MUST be implemented via this provider. Direct script injection is prohibited.
 *
 * CURRENT STATE (MVP):
 * This is a placeholder implementation. No tracking is active.
 * No external scripts are loaded. No consent management is implemented.
 *
 * FUTURE IMPLEMENTATION:
 * When analytics are introduced:
 * 1. Implement consent state management (default: tracking disabled)
 * 2. Load tracking scripts only after explicit user consent
 * 3. Allow consent withdrawal
 * 4. Optionally log consent server-side
 * 5. Maintain GDPR/revDSG compliance
 */

import type React from "react";

interface TrackingProviderProps {
  /** Child components */
  children: React.ReactNode;
}

/**
 * TrackingProvider component
 *
 * Currently a pass-through component that renders children without modification.
 * Serves as an architectural extension seam for future tracking implementation.
 */
export const TrackingProvider: React.FC<TrackingProviderProps> = ({ children }) => {
  return <>{children}</>;
};
