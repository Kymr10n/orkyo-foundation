import { logger } from "@foundation/src/lib/core/logger";

/**
 * localStorage for preferences. A private-mode browser, a full quota or a blocked origin
 * throws on access; losing a preference is acceptable, taking the page down for one is not.
 * So every access is guarded, a failure is logged, and a read answers `null`.
 *
 * Stores that persist a whole state use zustand `persist` instead; this is for the single
 * values a hook reads and writes itself.
 */
export const safeStorage = {
  get(key: string): string | null {
    try {
      return localStorage.getItem(key);
    } catch (err) {
      logger.error(`Could not read ${key} from storage`, err);
      return null;
    }
  },
  set(key: string, value: string): void {
    try {
      localStorage.setItem(key, value);
    } catch (err) {
      logger.error(`Could not write ${key} to storage`, err);
    }
  },
  remove(key: string): void {
    try {
      localStorage.removeItem(key);
    } catch (err) {
      logger.error(`Could not remove ${key} from storage`, err);
    }
  },
};
