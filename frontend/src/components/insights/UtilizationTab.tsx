import { useQueries } from '@tanstack/react-query';
import { UtilizationTrendChart } from '@foundation/src/components/insights/InsightsTrendCharts';
import { useInsightsTabContext } from '@foundation/src/components/insights/insightsTabContext';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import { EmptyState } from '@foundation/src/components/ui/EmptyState';
import { qk } from '@foundation/src/lib/api/query-keys';
import { STALE } from '@foundation/src/lib/core/query-client';
import { getInsightsUtilization } from '@foundation/src/lib/api/insights-api';
import { LineChart } from 'lucide-react';

/**
 * One utilization trend per resource type — but only for the types the selected site actually
 * holds.
 *
 * A type with no resources here draws no card. A frame reading "no capacity configured" over a
 * site that simply has no mills sends the reader hunting for a setting to change, and it can never
 * fill in while that site is selected. `resourceCount` is what separates that from the real
 * capacity case (resources present, capacity nets to zero), which keeps its message.
 *
 * `useQueries` rather than a child component per type: the number of types is runtime data, and
 * this is the hook built for a dynamic list of them. It also lets the tab see every answer at
 * once, which is what makes the "nothing here at all" state possible to render.
 */
export function UtilizationTab() {
  const { from, to, bucket, siteId } = useInsightsTabContext();
  // Active types only: an inactive type is out of planning, so its series would be a flat line.
  const { data: resourceTypes = [] } = useResourceTypes(true);

  const results = useQueries({
    queries: resourceTypes.map((type) => ({
      queryKey: qk.insights.utilization(type.key, siteId, from, to, bucket),
      queryFn: () => getInsightsUtilization(type.key, from, to, bucket, siteId),
      staleTime: STALE.ANALYTICS,
    })),
  });

  // Hidden only on an explicit zero. A card is kept while its query is in flight, so the grid
  // does not reflow as answers land — and kept too if `resourceCount` is missing, which is what an
  // API build older than this field returns. Failing closed there blanks the whole page over one
  // absent number, which is a far worse answer than showing a chart the reader can judge.
  const cards = resourceTypes
    .map((type, i) => ({ type, result: results[i] }))
    .filter(({ result }) => result?.data?.resourceCount !== 0);

  const settled = results.length > 0 && results.every((r) => !r.isLoading);

  if (settled && cards.length === 0) {
    return (
      <EmptyState
        icon={<LineChart className="h-8 w-8" />}
        message="This site has no resources yet, so there is nothing to chart. Add resources to it, or choose another site."
      />
    );
  }

  return (
    <div className="h-full space-y-4 overflow-auto p-1">
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        {cards.map(({ type, result }) => (
          <UtilizationTrendChart
            key={type.key}
            title={`${type.displayNamePlural} utilization trend`}
            data={result?.data}
            bucket={bucket}
            isLoading={result?.isLoading ?? true}
            error={result?.error}
          />
        ))}
      </div>
    </div>
  );
}
