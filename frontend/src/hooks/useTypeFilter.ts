import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import { safeStorage } from '@foundation/src/lib/core/safe-storage';
import { STORAGE_KEYS } from '@foundation/src/constants/storage';

/**
 * The set of resource types a grid tab is showing, held in `?stationTypes=mill,drill`.
 *
 * The URL is the source of truth so a filtered view is shareable and survives reload. The last
 * choice is mirrored to localStorage, per tab, and supplies the default when the URL says nothing
 * — which is what makes "I always look at mills" stick without turning the address bar into state
 * nobody asked for.
 *
 * An empty selection means every type: a filter that can hide everything is a way to look at an
 * empty grid and think the data is gone. Absent means the same, which is why "everything" is
 * stored as absence rather than as the expanded list — a list would freeze the set, and a type
 * added later would be filtered out by a choice nobody made.
 *
 * Each tab owns its own parameter. One shared `types` key would let a station selection be read,
 * rejected as unknown and silently reset by the assets tab.
 */
export function useTypeFilter(
  paramName: string,
  available: readonly ResourceTypeInfo[],
): [string[], (keys: string[]) => void] {
  const [searchParams, setSearchParams] = useSearchParams();
  const storageKey = `${STORAGE_KEYS.TYPE_FILTER_PREFIX}${paramName}`;

  const raw = searchParams.get(paramName);

  const selected = useMemo(() => {
    const availableKeys = new Set(available.map((t) => t.key));
    const fromUrl = raw ? raw.split(',').filter(Boolean) : null;
    const source = fromUrl ?? readStored(storageKey);
    // A key the tenant has since deactivated would filter the grid down to nothing with no way
    // to tell why, so unknown keys drop out here.
    const kept = (source ?? []).filter((k) => availableKeys.has(k));
    return kept.length > 0 ? kept : available.map((t) => t.key);
  }, [raw, available, storageKey]);

  const setSelected = useCallback(
    (keys: string[]) => {
      const next = new URLSearchParams(searchParams);
      const isEverything = keys.length === 0 || keys.length === available.length;
      // Everything is the default, so it is recorded as absence in both places rather than as a
      // pinned list — see the note above.
      if (isEverything) {
        next.delete(paramName);
        safeStorage.remove(storageKey);
      } else {
        next.set(paramName, keys.join(','));
        safeStorage.set(storageKey, JSON.stringify(keys));
      }
      setSearchParams(next, { replace: true });
    },
    [searchParams, setSearchParams, available.length, paramName, storageKey],
  );

  return [selected, setSelected];
}

function readStored(key: string): string[] | null {
  const raw = safeStorage.get(key);
  if (!raw) return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter((v): v is string => typeof v === 'string') : null;
  } catch {
    // A corrupted entry falls back to "every type" rather than taking the page down.
    return null;
  }
}
