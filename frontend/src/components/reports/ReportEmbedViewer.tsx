import { useEffect, useRef, useState } from 'react';
import { embedDashboard } from '@superset-ui/embedded-sdk';
import { useQuery } from '@tanstack/react-query';
import { createReportEmbedToken } from '@foundation/src/lib/api/reports-api';
import { ApiError } from '@foundation/src/lib/core/api-utils';
import { AlertCircle, Loader2, WifiOff } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';

interface ReportEmbedViewerProps {
  reportKey: string;
}

export function ReportEmbedViewer({ reportKey }: ReportEmbedViewerProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [embedError, setEmbedError] = useState<string | null>(null);

  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['report-embed-token', reportKey],
    queryFn: () => createReportEmbedToken(reportKey),
    staleTime: 0,
    gcTime: 0,
  });

  useEffect(() => {
    if (!data || !containerRef.current) return;

    const container = containerRef.current;
    setEmbedError(null);
    let unmounted = false;

    const url = new URL(data.embedUrl);
    const supersetDomain = url.origin;
    const dashboardId = url.pathname.split('/').pop()!;

    embedDashboard({
      id: dashboardId,
      supersetDomain,
      mountPoint: container,
      fetchGuestToken: async () => {
        const result = await createReportEmbedToken(reportKey);
        return result.token;
      },
      dashboardUiConfig: {
        hideTitle: true,
        hideChartControls: false,
      },
    }).catch((err: unknown) => {
      if (!unmounted) {
        setEmbedError(err instanceof Error ? err.message : 'Failed to load report');
      }
    });

    return () => {
      unmounted = true;
      container.innerHTML = '';
    };
  }, [data, reportKey]);

  if (isLoading) {
    return (
      <div className="flex flex-1 items-center justify-center py-24">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (isError || embedError) {
    const isNotProvisioned =
      !embedError && error instanceof ApiError && error.status === 503;

    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-3 py-24 text-center">
        {isNotProvisioned
          ? <WifiOff className="h-10 w-10 text-muted-foreground/40" />
          : <AlertCircle className="h-10 w-10 text-destructive/60" />
        }
        <div>
          <p className="font-medium">
            {isNotProvisioned ? 'Reports not yet available' : 'Reports are currently unavailable'}
          </p>
          <p className="text-sm text-muted-foreground mt-1">
            {isNotProvisioned
              ? 'Reporting has not been set up for this tenant. Contact your administrator.'
              : 'Core scheduling functionality is not affected.'
            }
          </p>
        </div>
        {!isNotProvisioned && (
          <Button variant="outline" size="sm" onClick={() => { setEmbedError(null); refetch(); }}>
            Retry
          </Button>
        )}
      </div>
    );
  }

  return (
    <div
      ref={containerRef}
      className="flex-1 w-full [&_iframe]:w-full [&_iframe]:h-full [&_iframe]:border-0"
      style={{ minHeight: '600px' }}
    />
  );
}
