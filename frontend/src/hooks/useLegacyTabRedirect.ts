import { useEffect } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';

/**
 * Redirects `?tab=<legacy>` query-param routes to their new path-based equivalents.
 * Used during the deprecation window for pages that moved from query-param tabs
 * (e.g. `/people?tab=jobTitles`) to nested routes (`/people/job-titles`).
 *
 * The map keys are legacy `?tab=` values; the values are absolute target paths.
 * Declare the map at module scope so the reference is stable across renders.
 */
export function useLegacyTabRedirect(map: Record<string, string>): void {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();

  useEffect(() => {
    const legacy = searchParams.get('tab');
    if (legacy && map[legacy]) {
      navigate(map[legacy], { replace: true });
    }
  }, [searchParams, navigate, map]);
}
