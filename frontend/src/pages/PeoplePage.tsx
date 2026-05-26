import { Outlet, useNavigate } from 'react-router-dom';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';

const TABS: PageTab[] = [
  { value: 'list', label: 'People' },
  { value: 'teams', label: 'Teams' },
  { value: 'job-titles', label: 'Job Titles' },
  { value: 'departments', label: 'Departments' },
];

export function PeoplePage() {
  const active = useActiveTab('list');
  const navigate = useNavigate();

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
