import { BarChart3 } from 'lucide-react';
import { Link } from 'react-router-dom';
import type { ReportDefinition } from '@foundation/src/lib/api/reports-api';

interface ReportCardProps {
  report: ReportDefinition;
}

export function ReportCard({ report }: ReportCardProps) {
  return (
    <Link
      to={`/reports/${report.key}`}
      className="flex flex-col gap-3 rounded-lg border bg-card p-5 hover:bg-accent/50 transition-colors"
    >
      <div className="flex items-center gap-3">
        <div className="flex h-9 w-9 items-center justify-center rounded-md bg-primary/10 text-primary">
          <BarChart3 className="h-5 w-5" />
        </div>
        <div className="min-w-0">
          <p className="font-medium leading-tight">{report.title}</p>
          <p className="text-xs text-muted-foreground capitalize">{report.category}</p>
        </div>
      </div>
      <p className="text-sm text-muted-foreground leading-relaxed">{report.description}</p>
    </Link>
  );
}
