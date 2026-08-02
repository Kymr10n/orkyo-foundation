import { Navigate, Outlet, useNavigate, useParams } from 'react-router';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';

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
  const { data: types = [], isLoading } = useResourceTypes();
  const navigate = useNavigate();
  // The tab sits one segment deeper here than on the fixed pages: /resources/<key>/<tab>.
  const active = useActiveTab('list', 3);

  const resourceType = types.find((t) => t.key === typeKey);
  usePageTitle(resourceType?.displayName ?? 'Resources');

  if (isLoading) return <LoadingSpinner message="Loading…" />;
  // An unknown or removed type key is a dead link — send the user somewhere real.
  if (!resourceType) return <Navigate to="/" replace />;

  return (
    <PageLayout>
      <PageHeader
        title={resourceType.displayName}
        description={resourceType.description || `Manage ${resourceType.displayName.toLowerCase()} and groups`}
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
