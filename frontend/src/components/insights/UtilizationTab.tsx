import { UtilizationTrendChart } from '@foundation/src/components/insights/InsightsTrendCharts';
import { useInsightsUtilization } from '@foundation/src/hooks/useInsights';
import { useInsightsTabContext } from '@foundation/src/components/insights/insightsTabContext';

export function UtilizationTab() {
  const { from, to, bucket, siteId } = useInsightsTabContext();
  const spaceUtil = useInsightsUtilization('space', siteId, from, to, bucket);
  const peopleUtil = useInsightsUtilization('person', siteId, from, to, bucket);

  return (
    <div className="h-full space-y-4 overflow-auto p-1">
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        <UtilizationTrendChart
          title="Space utilization trend"
          data={spaceUtil.data}
          bucket={bucket}
          isLoading={spaceUtil.isLoading}
          error={spaceUtil.error}
        />
        <UtilizationTrendChart
          title="People utilization trend"
          data={peopleUtil.data}
          bucket={bucket}
          isLoading={peopleUtil.isLoading}
          error={peopleUtil.error}
        />
      </div>
    </div>
  );
}
