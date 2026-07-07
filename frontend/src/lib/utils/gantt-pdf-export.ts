/**
 * PDF Export for Utilization Gantt Chart
 * Generates a visual representation of scheduled requests
 */

import jsPDF from 'jspdf';
import type { Request, RequestStatus } from '@foundation/src/types/requests';
import type { Space } from '@foundation/src/types/space';
import { format } from 'date-fns';
import { DATE_FORMATS } from '@foundation/src/lib/formatters';
import { getSpaceResourceId } from '@foundation/src/domain/scheduling/request-assignments';
import { REQUEST_STATUS_ORDER } from '@foundation/src/constants/request-status';
import { formatStatusLabel } from '@foundation/src/lib/utils/utils';

/** Single status→RGB(jsPDF) source for both the bars and the legend, keyed over every status so a
 *  new/renamed status can't render unmapped or drop out of the legend. Order follows the canonical
 *  REQUEST_STATUS_ORDER; labels come from the shared formatStatusLabel. */
const STATUS_RGB: Record<RequestStatus, [number, number, number]> = {
  new: [59, 130, 246],          // blue
  in_progress: [249, 115, 22],  // orange
  done: [34, 197, 94],          // green
  deferred: [100, 116, 139],    // slate
  cancelled: [156, 163, 175],   // gray
};

interface GanttExportOptions {
  requests: Request[];
  spaces: Space[];
  startDate: Date;
  endDate: Date;
  filename?: string;
}

export function exportGanttChartToPDF(options: GanttExportOptions) {
  const { requests, spaces, startDate, endDate, filename } = options;

  // Create PDF in landscape mode
  const doc = new jsPDF({
    orientation: 'landscape',
    unit: 'mm',
    format: 'a4',
  });

  const pageWidth = doc.internal.pageSize.getWidth();
  const pageHeight = doc.internal.pageSize.getHeight();
  const margin = 15;
  const contentWidth = pageWidth - (2 * margin);
  const contentHeight = pageHeight - (2 * margin);

  // Header
  doc.setFontSize(18);
  doc.setFont('helvetica', 'bold');
  doc.text('Space Utilization Gantt Chart', margin, margin + 10);

  doc.setFontSize(10);
  doc.setFont('helvetica', 'normal');
  doc.text(
    `Period: ${format(startDate, DATE_FORMATS.DATE_MEDIUM)} - ${format(endDate, DATE_FORMATS.DATE_MEDIUM)}`,
    margin,
    margin + 17
  );
  doc.text(
    `Generated: ${format(new Date(), DATE_FORMATS.DATETIME_MEDIUM)}`,
    margin,
    margin + 22
  );

  // Filter to only scheduled requests
  const scheduledRequests = requests.filter(r => r.startTs && r.endTs && getSpaceResourceId(r));

  // Calculate time range
  const timeRange = endDate.getTime() - startDate.getTime();
  const chartX = margin;
  const chartY = margin + 35;
  const chartWidth = contentWidth * 0.75; // 75% for chart, 25% for legend
  const chartHeight = contentHeight - 40;

  // Draw timeline
  doc.setFontSize(8);
  doc.setDrawColor(200, 200, 200);

  // Draw vertical grid lines for dates
  const days = Math.ceil(timeRange / (1000 * 60 * 60 * 24));
  const gridStep = Math.max(1, Math.floor(days / 10)); // Show ~10 grid lines

  for (let i = 0; i <= days; i += gridStep) {
    const date = new Date(startDate.getTime() + i * 24 * 60 * 60 * 1000);
    const x = chartX + (i / days) * chartWidth;

    doc.setDrawColor(220, 220, 220);
    doc.line(x, chartY, x, chartY + chartHeight);

    doc.setTextColor(100, 100, 100);
    doc.text(format(date, DATE_FORMATS.DATE_HEADER), x, chartY - 3, { align: 'center' });
  }

  // Group requests by space
  const spaceGroups = new Map<string, Request[]>();
  scheduledRequests.forEach(request => {
    const resourceId = getSpaceResourceId(request)!;
    if (!spaceGroups.has(resourceId)) {
      spaceGroups.set(resourceId, []);
    }
    spaceGroups.get(resourceId)!.push(request);
  });

  // Draw bars for each space
  const resourceIds = Array.from(spaceGroups.keys());
  const rowHeight = Math.min(12, chartHeight / Math.max(resourceIds.length, 1));
  const barHeight = rowHeight * 0.7;

  doc.setFontSize(8);

  resourceIds.forEach((resourceId, index) => {
    const space = spaces.find(s => s.id === resourceId);
    const spaceRequests = spaceGroups.get(resourceId)!;
    const y = chartY + index * rowHeight;

    // Draw space label
    doc.setTextColor(0, 0, 0);
    doc.text(
      space?.name || 'Unknown Space',
      margin,
      y + barHeight / 2 + 2,
      { align: 'right', maxWidth: chartX - margin - 5 }
    );

    // Draw request bars
    spaceRequests.forEach(request => {
      const requestStart = new Date(request.startTs!).getTime();
      const requestEnd = new Date(request.endTs!).getTime();

      // Calculate bar position
      const startRatio = (requestStart - startDate.getTime()) / timeRange;
      const endRatio = (requestEnd - startDate.getTime()) / timeRange;
      const barX = chartX + startRatio * chartWidth;
      const barWidth = (endRatio - startRatio) * chartWidth;

      // Color based on status (shared STATUS_RGB source — see top of file)
      const color = STATUS_RGB[request.status] ?? [150, 150, 150];
      doc.setFillColor(...color);
      doc.setDrawColor(...color);
      doc.roundedRect(barX, y, barWidth, barHeight, 1, 1, 'F');

      // Add request name if bar is wide enough
      if (barWidth > 20) {
        doc.setTextColor(255, 255, 255);
        doc.setFontSize(7);
        doc.text(
          request.name,
          barX + 2,
          y + barHeight / 2 + 1.5,
          { maxWidth: barWidth - 4 }
        );
      }
    });
  });

  // Legend
  const legendX = chartX + chartWidth + 10;
  const legendY = chartY;

  doc.setFontSize(10);
  doc.setFont('helvetica', 'bold');
  doc.setTextColor(0, 0, 0);
  doc.text('Status Legend', legendX, legendY);

  doc.setFont('helvetica', 'normal');
  doc.setFontSize(8);

  REQUEST_STATUS_ORDER.forEach((status, index) => {
    const y = legendY + 8 + index * 7;

    // Color box
    doc.setFillColor(...STATUS_RGB[status]);
    doc.roundedRect(legendX, y - 3, 5, 4, 0.5, 0.5, 'F');

    // Label
    doc.setTextColor(0, 0, 0);
    doc.text(formatStatusLabel(status), legendX + 7, y);
  });

  // Statistics
  const statsY = legendY + 60;
  doc.setFont('helvetica', 'bold');
  doc.text('Statistics', legendX, statsY);

  doc.setFont('helvetica', 'normal');
  doc.text(`Total Requests: ${scheduledRequests.length}`, legendX, statsY + 7);
  doc.text(`Spaces Used: ${resourceIds.length}`, legendX, statsY + 14);
  doc.text(`Period: ${days} days`, legendX, statsY + 21);

  // Footer
  doc.setFontSize(8);
  doc.setTextColor(150, 150, 150);
  doc.text(
    'Utilzing Space',
    pageWidth / 2,
    pageHeight - 5,
    { align: 'center' }
  );

  // Save the PDF
  const pdfFilename = filename || `gantt-chart-${format(new Date(), DATE_FORMATS.DATE_ISO)}.pdf`;
  doc.save(pdfFilename);
}
