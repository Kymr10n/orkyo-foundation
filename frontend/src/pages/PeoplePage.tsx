import { useEffect } from 'react';
import { Outlet, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { Tabs, TabsList, TabsTrigger } from '@foundation/src/components/ui/tabs';

// Tab value <-> sub-route path. Source of truth lives here so the trigger order
// and the URL segment cannot drift.
const TABS: { value: string; label: string }[] = [
  { value: 'list', label: 'People' },
  { value: 'groups', label: 'Groups' },
  { value: 'absences', label: 'Absences' },
  { value: 'job-titles', label: 'Job Titles' },
  { value: 'departments', label: 'Departments' },
  { value: 'skills', label: 'Skills' },
];

// Map legacy ?tab= values (pre PR 2, query-param routing) to the new path segment.
// TODO 2026-08-17: remove this redirect after one release cycle.
const LEGACY_TAB_REDIRECTS: Record<string, string> = {
  people: 'list',
  groups: 'groups',
  absences: 'absences',
  jobTitles: 'job-titles',
  departments: 'departments',
  skills: 'skills',
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
    <div className="flex flex-col h-full p-4 md:p-6 lg:p-8">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold">People</h1>
          <p className="text-sm text-muted-foreground">
            Manage person resources, groups, and absences
          </p>
        </div>
      </div>

      <Tabs value={active} onValueChange={(v) => navigate(`/people/${v}`)} className="flex-1 flex flex-col">
        <TabsList className="mb-4">
          {TABS.map((t) => (
            <TabsTrigger key={t.value} value={t.value}>{t.label}</TabsTrigger>
          ))}
        </TabsList>

        <div className="flex-1">
          <Outlet />
        </div>
      </Tabs>
    </div>
  );
}
