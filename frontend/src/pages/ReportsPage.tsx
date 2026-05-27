import { useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { getReports } from '@foundation/src/lib/api/reports-api';
import { ReportCard } from '@foundation/src/components/reports/ReportCard';
import { ReportEmbedViewer } from '@foundation/src/components/reports/ReportEmbedViewer';
import { PageLayout, PageHeader } from '@foundation/src/components/layout';
import { Loader2, BarChart3 } from 'lucide-react';

export function ReportsPage() {
  const { reportKey } = useParams<{ reportKey?: string }>();

  const { data: reports = [], isLoading } = useQuery({
    queryKey: ['reports'],
    queryFn: getReports,
  });

  if (reportKey) {
    const report = reports.find(r => r.key === reportKey);
    return (
      <PageLayout>
        <PageHeader
          title={report?.title ?? 'Report'}
          description={report?.description}
        />
        <div className="flex flex-col flex-1 min-h-0">
          <ReportEmbedViewer reportKey={reportKey} />
        </div>
      </PageLayout>
    );
  }

  return (
    <PageLayout>
      <PageHeader
        title="Reports"
        description="Embedded analytics for space utilization, requests, and resource allocation"
      />

      {isLoading ? (
        <div className="flex items-center justify-center py-24">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      ) : reports.length === 0 ? (
        <div className="flex flex-col items-center justify-center gap-3 py-24 text-center">
          <BarChart3 className="h-10 w-10 text-muted-foreground/40" />
          <p className="text-muted-foreground">No reports are available for your role or tenant.</p>
        </div>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {reports.map(report => (
            <ReportCard key={report.key} report={report} />
          ))}
        </div>
      )}
    </PageLayout>
  );
}
