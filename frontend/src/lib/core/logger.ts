/**
 * Centralized logging utility
 * Provides structured logging with levels and automatic stripping in production builds
 */

import { runtimeConfig } from '@/config/runtime';

const isDevelopment = runtimeConfig.isDev;

export const logger = {
  /**
   * Debug information - stripped in production
   */
  debug: (...args: unknown[]) => {
    if (isDevelopment) {
      console.log("[DEBUG]", ...args);
    }
  },

  /**
   * Informational messages - stripped in production
   */
  info: (...args: unknown[]) => {
    if (isDevelopment) {
      console.log("[INFO]", ...args);
    }
  },

  /**
   * Warning messages - kept in production
   */
  warn: (...args: unknown[]) => {
    console.warn("[WARN]", ...args);
  },

  /**
   * Error messages - always logged
   */
  error: (...args: unknown[]) => {
    console.error("[ERROR]", ...args);
  },
};
