import { useEffect } from 'react';
import { Outlet, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';

// Tab value <-> sub-route path. Source of truth lives here so the trigger order
// and the URL segment cannot drift.
const TABS: PageTab[] = [
  { value: 'list', label: 'People' },
  { value: 'groups', label: 'Groups' },
  { value: 'absences', label: 'Absences' },
  { value: 'job-titles', label: 'Job Titles' },
  { value: 'departments', label: 'Departments' },
];

// Map legacy ?tab= values (pre PR 2, query-param routing) to the new path segment.
// TODO 2026-08-17: remove this redirect after one release cycle.
const LEGACY_TAB_REDIRECTS: Record<string, string> = {
  people: 'list',
  groups: 'groups',
  absences: 'absences',
  jobTitles: 'job-titles',
  departments: 'departments',
};

export function PeoplePage() {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  // Backward-compat redirect: /people?tab=jobTitles -> /people/job-titles
  useEffect(() => {
    const legacy = searchParams.get('tab');
    if (legacy && LEGACY_TAB_REDIRECTS[legacy]) {
      navigate(`/people/${LEGACY_TAB_REDIRECTS[legacy]}`, { replace: true });
    }
  }, [searchParams, navigate]);

  // Active tab is the first path segment after /people/...
  const active = pathname.split('/')[2] ?? 'list';

  return (
    <PageLayout>
      <PageHeader
        title="People"
        description="Manage person resources, groups, and absences"
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
