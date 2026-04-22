/**
 * Centralized error handling utility
 * Provides consistent error handling patterns across the application
 */

import { logger } from "./logger";

interface ErrorContext {
  component?: string;
  operation?: string;
  data?: Record<string, unknown>;
}

/**
 * Handles errors with consistent logging and optional user-facing messages
 */
export function handleError(
  error: unknown,
  context?: ErrorContext,
  userMessage?: string,
): string {
  const errorMessage = error instanceof Error ? error.message : String(error);

  // Log the full error with context
  logger.error(
    `[${context?.component || "App"}] ${context?.operation || "Operation"} failed:`,
    errorMessage,
    context?.data,
  );

  // Return user-facing message or generic fallback
  return userMessage || "An unexpected error occurred. Please try again.";
}

/**
 * Wraps an async function with error handling
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function withErrorHandler<T extends (...args: any[]) => Promise<any>>(
  fn: T,
  context: ErrorContext,
  userMessage?: string,
): T {
  return (async (...args: Parameters<T>) => {
    try {
      return await fn(...args);
    } catch (error) {
      throw new Error(handleError(error, context, userMessage));
    }
  }) as T;
}

/**
 * Extracts error message from various error types
 */
export function getErrorMessage(error: unknown): string {
  if (error instanceof Error) {
    return error.message;
  }
  if (typeof error === "string") {
    return error;
  }
  if (error && typeof error === "object" && "message" in error) {
    return String(error.message);
  }
  return "An unknown error occurred";
}
