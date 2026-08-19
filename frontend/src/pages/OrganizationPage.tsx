import { useMemo } from 'react';
import { PageLayout, PageHeader } from '@foundation/src/components/layout';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import {
  SharedListRowsPanel,
  type SharedListEntry,
} from '@foundation/src/components/lists/SharedListRowsPanel';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';
import { useListDefinitions } from '@foundation/src/hooks/useListDefinitions';

/**
 * Organization master data — departments, job titles, cost centres and anything else the tenant
 * models about its own structure.
 *
 * Each entry is an organization-scoped list definition, seeded by migration 1820, so this page is
 * a selector over those definitions and the rows behind the chosen one. It edits values, not
 * shapes: adding a column, or a whole new kind of organization list, is administration and lives
 * in Configuration.
 */
export function OrganizationPage() {
  usePageTitle('Organization');
  const { data: definitions = [], isLoading, error } = useListDefinitions();

  const entries = useMemo<SharedListEntry[]>(
    () =>
      definitions
        .filter((d) => d.scope === 'organization' && d.isActive)
        .map((d) => ({ id: d.id, label: d.name, definitionId: d.id })),
    [definitions],
  );

  if (isLoading) return <LoadingSpinner message="Loading…" />;

  return (
    <PageLayout>
      <PageHeader
        title="Organization"
        description="Departments, job titles and the rest of the organization's shared reference data"
      />

      <ErrorAlert message={error instanceof Error ? error.message : null} />

      <SharedListRowsPanel
        entries={entries}
        selectId="organization-list"
        emptyMessage="No organization lists yet. Organization lists hold shared data such as departments and job titles; an administrator creates them under Configuration → List definitions."
      />
    </PageLayout>
  );
}
