import { UtilizationTrendChart } from '@foundation/src/components/insights/InsightsTrendCharts';
import { useInsightsUtilization } from '@foundation/src/hooks/useInsights';
import { useInsightsTabContext } from '@foundation/src/components/insights/insightsTabContext';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import type { InsightsBucket } from '@foundation/src/lib/api/insights-api';

/**
 * One trend chart for one resource type.
 *
 * A child component rather than a loop of hooks in the parent: how many types a tenant has is
 * runtime data and can change between renders, which the rules of hooks forbid. Giving each type
 * its own component instance gives each its own stable hook call.
 */
function ResourceTypeTrend({
  resourceTypeKey,
  displayNamePlural,
  siteId,
  from,
  to,
  bucket,
}: {
  resourceTypeKey: string;
  displayNamePlural: string;
  siteId: string | null;
  from: Date;
  to: Date;
  bucket: InsightsBucket;
}) {
  const util = useInsightsUtilization(resourceTypeKey, siteId, from, to, bucket);
  return (
    <UtilizationTrendChart
      title={`${displayNamePlural} utilization trend`}
      data={util.data}
      bucket={bucket}
      isLoading={util.isLoading}
      error={util.error}
    />
  );
}

export function UtilizationTab() {
  const { from, to, bucket, siteId } = useInsightsTabContext();
  // Active types only: an inactive type is out of planning, so its series would be a flat line.
  const { data: resourceTypes = [] } = useResourceTypes(true);

  return (
    <div className="h-full space-y-4 overflow-auto p-1">
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        {resourceTypes.map((type) => (
          <ResourceTypeTrend
            key={type.key}
            resourceTypeKey={type.key}
            displayNamePlural={type.displayNamePlural}
            siteId={siteId}
            from={from}
            to={to}
            bucket={bucket}
          />
        ))}
      </div>
    </div>
  );
}
