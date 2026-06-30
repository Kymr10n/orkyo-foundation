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

/**
 * The user's locale, resolved from the browser. The single source of truth for
 * locale-aware date/time formatting ({@link formatLocalized}) and for any library
 * that takes a locale code (e.g. FullCalendar's `locale={{ code: USER_LOCALE }}`).
 */
export const USER_LOCALE =
  typeof navigator !== "undefined" && navigator.language ? navigator.language : "en";

const intlCache = new Map<string, Intl.DateTimeFormat>();

/**
 * Locale-aware date/time formatting via the native `Intl` API. Unlike a fixed
 * date-fns token string, this respects the user's local settings — 12h vs 24h
 * time, date ordering, month/weekday names — per {@link USER_LOCALE}. Formatters
 * are cached because constructing `Intl.DateTimeFormat` is comparatively expensive
 * and these run per grid column / per render.
 */
export function formatLocalized(date: Date, options: Intl.DateTimeFormatOptions): string {
  const key = JSON.stringify(options);
  let formatter = intlCache.get(key);
  if (!formatter) {
    formatter = new Intl.DateTimeFormat(USER_LOCALE, options);
    intlCache.set(key, formatter);
  }
  return formatter.format(date);
}

/**
 * Compact clock label following {@link USER_LOCALE} — "1am"/"12pm"/"1:15pm" in 12h locales,
 * "13"/"13:15" in 24h locales (e.g. de-DE). `withMinutes` includes the minutes (use it for sub-hour
 * columns; omit for whole-hour columns).
 *
 * This is the single source of truth for the scheduler grid's hour/minute column labels AND the
 * calendar's slot-axis + event-block times (via FullCalendar's `slotLabelContent`/`eventContent`
 * callbacks) — so the two grids render byte-identical times in every locale. The meridiem is
 * normalised to the calendar's compact lowercase style ("1 AM" → "1am"), handling the narrow no-break
 * space `Intl` inserts before AM/PM; 24h locales have no meridiem and are unaffected.
 */
export function formatCompactTime(date: Date, withMinutes: boolean): string {
  const options: Intl.DateTimeFormatOptions = withMinutes
    ? { hour: "numeric", minute: "2-digit" }
    : { hour: "numeric" };
  // Normalise the meridiem to the calendar's compact lowercase style ("1 AM" -> "1am"). The class
  // includes the no-break (U+00A0) / narrow no-break (U+202F) spaces Intl may insert before AM/PM;
  // 24h locales have no meridiem, so this is a no-op there.
  return formatLocalized(date, options).replace(
    /[\s  ]*([AP]M)$/i,
    (_, meridiem: string) => meridiem.toLowerCase(),
  );
}

/** Shared day-column / week-day-header options so the grid and calendar headers can't drift. */
export const GRID_DAY_HEADER_OPTS: Intl.DateTimeFormatOptions = { weekday: "short", day: "2-digit" };
/** Shared week-column header options (month + day). */
export const GRID_WEEK_HEADER_OPTS: Intl.DateTimeFormatOptions = { month: "short", day: "2-digit" };
