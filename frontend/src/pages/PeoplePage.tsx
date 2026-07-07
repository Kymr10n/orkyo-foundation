import { Outlet, useNavigate } from 'react-router-dom';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';

const TABS: PageTab[] = [
  { value: 'list', label: 'People' },
  { value: 'teams', label: 'Teams' },
  { value: 'job-titles', label: 'Job Titles' },
  { value: 'departments', label: 'Departments' },
];

export function PeoplePage() {
  usePageTitle('People');
  const active = useActiveTab('list');
  const navigate = useNavigate();

  return (
    <PageLayout>
      <PageHeader
        title="People"
        description="Manage people, teams, and absences"
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
