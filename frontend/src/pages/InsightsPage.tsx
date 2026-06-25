import { Outlet, useNavigate } from 'react-router-dom';
import { useMemo, useState } from 'react';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { InsightsFilters, resolveRange, type RangePreset } from '@foundation/src/components/insights/InsightsFilters';
import type { InsightsTabContext } from '@foundation/src/components/insights/insightsTabContext';
import type { InsightsBucket } from '@foundation/src/lib/api/insights-api';
import { useAppStore } from '@foundation/src/store/app-store';

const TABS: PageTab[] = [
  { value: 'overview', label: 'Overview' },
  { value: 'utilization', label: 'Utilization' },
  { value: 'conflicts', label: 'Conflicts' },
];

/**
 * Insights — Overview / Utilization / Conflicts. A thin shell that owns the shared range/bucket
 * filter and hands it to the active tab via <Outlet context>. Each tab is its own route, so only
 * the active tab's /api/insights/* queries run (no 5-call burst on load).
 */
export function InsightsPage() {
  const active = useActiveTab('overview');
  const navigate = useNavigate();
  const siteId = useAppStore((s) => s.selectedSiteId);
  const [range, setRange] = useState<RangePreset>('window');
  const [bucket, setBucket] = useState<InsightsBucket>('month');

  // Anchor the window when the preset changes — not on every render, so query keys stay stable.
  const { from, to } = useMemo(() => resolveRange(range), [range]);

  const ctx: InsightsTabContext = { from, to, bucket, siteId };

  return (
    <PageLayout>
      <PageHeader title="Insights" description="Conflicts and utilization analytics" />
      <div className="mb-4">
        <InsightsFilters range={range} onRangeChange={setRange} bucket={bucket} onBucketChange={setBucket} />
      </div>
      <PageTabs tabs={TABS} value={active} onChange={(v) => navigate(`/insights/${v}`)}>
        <Outlet context={ctx} />
      </PageTabs>
    </PageLayout>
  );
}
