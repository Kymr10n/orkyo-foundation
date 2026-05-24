import { Outlet, useNavigate } from 'react-router-dom';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { useLegacyTabRedirect } from '@foundation/src/hooks/useLegacyTabRedirect';

// Tab value <-> sub-route path. Source of truth lives here so the trigger order
// and the URL segment cannot drift.
const TABS: PageTab[] = [
  { value: 'list', label: 'People' },
  { value: 'teams', label: 'Teams' },
  { value: 'job-titles', label: 'Job Titles' },
  { value: 'departments', label: 'Departments' },
];

// Map legacy ?tab= values (pre PR 2, query-param routing) to the new absolute path.
// TODO 2026-08-17: remove this redirect after one release cycle.
const LEGACY_TAB_REDIRECTS: Record<string, string> = {
  people: '/people/list',
  groups: '/people/teams',
  jobTitles: '/people/job-titles',
  departments: '/people/departments',
};

export function PeoplePage() {
  const active = useActiveTab('list');
  const navigate = useNavigate();

  // Backward-compat redirect: /people?tab=jobTitles -> /people/job-titles
  useLegacyTabRedirect(LEGACY_TAB_REDIRECTS);

  return (
    <PageLayout>
      <PageHeader
        title="People"
        description="Manage person resources, teams, and absences"
      />
      <PageTabs
        tabs={TABS}
        value={active}
        onChange={(v) => navigate(`/people/${v}`)}
      >
        <Outlet />
      </PageTabs>
    </PageLayout>
  );
}
