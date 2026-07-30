import { Navigate, useParams } from 'react-router-dom';
import { PageLayout } from '@foundation/src/components/layout';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import { ResourceList } from '@foundation/src/components/resources/ResourceList';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';

/**
 * Generic management page for one resource type, addressed by its key (`/resources/car`).
 * The built-in types keep their purpose-built pages (Spaces, People); this serves every
 * type a tenant defines for itself.
 */
export function ResourcesPage() {
  const { typeKey } = useParams<{ typeKey: string }>();
  const { data: types = [], isLoading } = useResourceTypes();

  const resourceType = types.find((t) => t.key === typeKey);
  usePageTitle(resourceType?.displayName ?? 'Resources');

  if (isLoading) return <LoadingSpinner message="Loading…" />;
  // An unknown or removed type key is a dead link — send the user somewhere real.
  if (!resourceType) return <Navigate to="/" replace />;

  return (
    <PageLayout>
      <ResourceList resourceType={resourceType} />
    </PageLayout>
  );
}
