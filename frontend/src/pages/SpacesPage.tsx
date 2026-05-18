import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { Tabs, TabsList, TabsTrigger } from '@foundation/src/components/ui/tabs';

const TABS: { value: string; label: string }[] = [
  { value: 'list', label: 'Spaces' },
  { value: 'floorplan', label: 'Floorplan' },
  { value: 'groups', label: 'Groups' },
  { value: 'capabilities', label: 'Capabilities' },
];

export function SpacesPage() {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const active = pathname.split('/')[2] ?? 'list';

  return (
    <div className="flex flex-col h-full p-4 md:p-6 lg:p-8">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold">Spaces</h1>
          <p className="text-sm text-muted-foreground">
            Manage spaces, floorplan, groups, and capabilities
          </p>
        </div>
      </div>

      <Tabs value={active} onValueChange={(v) => navigate(`/spaces/${v}`)} className="flex-1 flex flex-col">
        <TabsList className="mb-4">
          {TABS.map((t) => (
            <TabsTrigger key={t.value} value={t.value}>{t.label}</TabsTrigger>
          ))}
        </TabsList>

        <div className="flex-1 min-h-0">
          <Outlet />
        </div>
      </Tabs>
    </div>
  );
}
