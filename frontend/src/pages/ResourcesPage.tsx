import { Navigate, Outlet, useNavigate, useParams } from 'react-router';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';
import {
  DEDICATED_TYPE_ROUTES,
  TYPES_WITH_DEDICATED_PAGES,
} from '@foundation/src/constants/resource-type-key';

const TABS: PageTab[] = [
  { value: 'list', label: 'List' },
  { value: 'groups', label: 'Groups' },
];

/**
 * Generic management page for one resource type, addressed by its key (`/resources/car`).
 * The built-in types keep their purpose-built pages (Spaces, People); this serves every
 * type a tenant defines for itself — with the same tabbed shape, so a custom type is not
 * visibly a lesser citizen.
 */
export function ResourcesPage() {
  const { typeKey } = useParams<{ typeKey: string }>();
  // isActive: true matches every other caller, so they share one cache entry.
  const { data: types = [], isLoading } = useResourceTypes(true);
  const navigate = useNavigate();
  // The tab sits one segment deeper here than on the fixed pages: /resources/<key>/<tab>.
  const active = useActiveTab('list', 3);

  const resourceType = types.find((t) => t.key === typeKey);
  usePageTitle(resourceType?.displayNamePlural ?? 'Resources');

  if (isLoading) return <LoadingSpinner message="Loading…" />;
  // Space and person have purpose-built pages; rendering them here too would show the same
  // rows under a second URL with fewer features. The page said so, nothing enforced it.
  if (typeKey && TYPES_WITH_DEDICATED_PAGES.has(typeKey)) {
    return <Navigate to={DEDICATED_TYPE_ROUTES[typeKey].list} replace />;
  }
  // An unknown or removed type key is a dead link — send the user somewhere real.
  if (!resourceType) return <Navigate to="/" replace />;

  return (
    <PageLayout>
      <PageHeader
        title={resourceType.displayNamePlural}
        description={resourceType.description || `Manage ${resourceType.displayNamePlural.toLowerCase()} and groups`}
      />
      <PageTabs
        tabs={TABS}
        value={active}
        onChange={(v) => navigate(`/resources/${resourceType.key}/${v}`)}
      >
        <Outlet context={{ resourceType }} />
      </PageTabs>
    </PageLayout>
  );
}
