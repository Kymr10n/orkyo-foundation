import { Navigate, useLocation, useParams } from 'react-router';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import { typeRoute } from '@foundation/src/constants/resource-class';

/**
 * Forwards a pre-class URL (`/resources/mill/list`, `/people/list`) to wherever that type lives
 * now. The class is not in the old URL, so the type has to be resolved before the destination is
 * known — which is why this is a component and not a static `<Navigate>`.
 */
export function LegacyTypeRedirect({
  tab = 'instances',
  typeKey: fixedKey,
}: {
  tab?: 'instances' | 'groups' | 'lists';
  /** For routes that name their type in the path rather than in a param, such as /people. */
  typeKey?: string;
}) {
  const params = useParams<{ typeKey: string }>();
  const { search } = useLocation();
  const key = fixedKey ?? params.typeKey;
  const { data: types = [], isLoading } = useResourceTypes(true);

  if (isLoading) return <LoadingSpinner message="Loading…" />;

  const type = types.find((t) => t.key === key);
  // A type that no longer exists is a dead link; root is somewhere real.
  if (!type) return <Navigate to="/" replace />;

  // The query string carries the deep link — `?edit=<id>` from global search — so it has to
  // survive the hop. Dropping it would turn every search hit into a plain list view.
  return <Navigate to={`${typeRoute(type, tab)}${search}`} replace />;
}
