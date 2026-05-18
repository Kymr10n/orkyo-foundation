/**
 * Centralized date format tokens for date-fns `format()`.
 *
 * Use these instead of inline string literals so that locale/format changes
 * happen in one place. Existing call sites can be migrated incrementally.
 *
 * Usage:
 *   import { format } from 'date-fns';
 *   import { DATE_FORMATS } from '@foundation/src/lib/formatters';
 *   format(date, DATE_FORMATS.DATE_LOCALE_SHORT)
 */
export const DATE_FORMATS = {
  /** Locale-aware short date. Renders as "Oct 14, 2025" in en-US. */
  DATE_LOCALE_SHORT: "PP",
  /** Fixed medium date. "Oct 14, 2025" */
  DATE_MEDIUM: "MMM d, yyyy",
  /** Compact date for scheduler headers and labels. "Oct 14" */
  DATE_HEADER: "MMM d",
  /** 24-hour time. "14:30" */
  TIME_24H: "HH:mm",
  /** Medium datetime. "Oct 14, 2025 14:30" */
  DATETIME_MEDIUM: "MMM d, yyyy HH:mm",
  /** ISO date string, safe for filenames and input[type=date]. "2025-10-14" */
  DATE_ISO: "yyyy-MM-dd",
  /** Full year only. "2025" */
  YEAR: "yyyy",
} as const;

export type DateFormatKey = keyof typeof DATE_FORMATS;
