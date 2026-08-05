/**
 * PDF Export for Utilization Gantt Chart
 * Generates a visual representation of scheduled requests
 */

import jsPDF from 'jspdf';
import type { Request, RequestStatus, ResourceAssignment } from '@foundation/src/types/requests';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import { format } from 'date-fns';
import { DATE_FORMATS } from '@foundation/src/lib/formatters';
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

/** Non-cancelled assignments — the ones that actually occupy a resource. */
function liveAssignments(request: Request) {
  return (request.assignments ?? []).filter((a) => a.assignmentStatus !== 'Cancelled');
}

// ── Layout (mm, A4 landscape 297×210) ─────────────────────────────────────────
// Fixed, readable geometry: the row height never shrinks to fit — the chart
// paginates instead. 8pt text is ~2.8mm tall, so 7mm rows stay legible.
const MARGIN = 15;
/** Left column reserved for resource names; the timeline starts after it. */
const LABEL_GUTTER = 45;
const ROW_HEIGHT = 7;
const BAR_HEIGHT = ROW_HEIGHT * 0.7;
/** Top of the row area: header block ends ~37, legend strip at ~44, axis labels at chartY−3. */
const CHART_Y = 52;
/** Bottom of the row area; keeps clear of the footer baseline at pageHeight−5. */
const CHART_BOTTOM = 198;
const ROWS_PER_PAGE = Math.floor((CHART_BOTTOM - CHART_Y) / ROW_HEIGHT);

/** What the chart knows about a resource: its label and which type's section it belongs in. */
export interface GanttResource {
  name: string;
  typeKey: string;
}

interface GanttExportOptions {
  requests: Request[];
  /** resourceId → name + type, for every resource type (not just spaces). */
  resources: Map<string, GanttResource>;
  /**
   * The types to render, in display order — one section per type, each starting a new page.
   * The caller decides the scope: the active tab's type alone, or every type from the
   * Calendar tab. Tenant-defined types are ordinary members of this list.
   */
  resourceTypes: ResourceTypeInfo[];
  startDate: Date;
  endDate: Date;
  filename?: string;
}

/** One resource's row within a section. */
interface GanttRow {
  resourceId: string;
  name: string;
  entries: { request: Request; assignment: ResourceAssignment }[];
}

export function exportGanttChartToPDF(options: GanttExportOptions) {
  const { requests, resources, resourceTypes, startDate, endDate, filename } = options;

  // Create PDF in landscape mode
  const doc = new jsPDF({
    orientation: 'landscape',
    unit: 'mm',
    format: 'a4',
  });

  const pageWidth = doc.internal.pageSize.getWidth();
  const pageHeight = doc.internal.pageSize.getHeight();
  const chartX = MARGIN + LABEL_GUTTER;
  const chartWidth = pageWidth - chartX - MARGIN;

  const startMs = startDate.getTime();
  const endMs = endDate.getTime();
  const timeRange = endMs - startMs;
  const days = Math.ceil(timeRange / (1000 * 60 * 60 * 24));

  // Scheduled = has a time window and at least one live assignment. This used
  // to require a SPACE assignment, so a request booked onto a person or a tool
  // vanished from the chart — an empty PDF on any tenant scheduling both.
  const scheduledRequests = requests.filter(
    (r) => r.startTs && r.endTs && liveAssignments(r).length > 0,
  );

  /**
   * The rows of one type's section: one per resource, one bar per assignment — the same
   * shape the on-screen grid renders (a request occupying a room for one day of its week
   * shows a one-day bar on that room's row). A request on a space AND a person appears in
   * both sections — that is what it does to those resources. Only assignments overlapping
   * the export window count; the caller hands us its whole buffered fetch window, most of
   * which lies outside the visible period.
   */
  const rowsForType = (typeKey: string): GanttRow[] => {
    const byResource = new Map<string, GanttRow>();

    scheduledRequests.forEach((request) => {
      for (const assignment of liveAssignments(request)) {
        if (assignment.resourceTypeKey !== typeKey) continue;

        const aStart = new Date(assignment.startUtc).getTime();
        const aEnd = new Date(assignment.endUtc).getTime();
        if (aEnd <= startMs || aStart >= endMs) continue;

        const row = byResource.get(assignment.resourceId);
        if (row) row.entries.push({ request, assignment });
        else byResource.set(assignment.resourceId, {
          resourceId: assignment.resourceId,
          name: resources.get(assignment.resourceId)?.name || 'Unknown resource',
          entries: [{ request, assignment }],
        });
      }
    });

    return Array.from(byResource.values()).sort((a, b) => a.name.localeCompare(b.name));
  };

  // A type with nothing scheduled contributes no pages — an empty section would
  // just be a page of chrome.
  const sections = resourceTypes
    .map((type) => ({ type, rows: rowsForType(type.key) }))
    .filter((section) => section.rows.length > 0);

  const totalPages = Math.max(
    1,
    sections.reduce((sum, s) => sum + Math.ceil(s.rows.length / ROWS_PER_PAGE), 0),
  );

  /** Header, section title, legend strip, timeline axis and footer — repeated on every page. */
  const drawPageChrome = (
    page: number,
    section: { type: ResourceTypeInfo; rows: GanttRow[] } | null,
    isSectionStart: boolean,
    rowsOnPage: number,
  ) => {
    // Header
    doc.setFontSize(18);
    doc.setFont('helvetica', 'bold');
    doc.setTextColor(0, 0, 0);
    doc.text('Utilization Gantt Chart', MARGIN, MARGIN + 10);

    // Which resource type this page covers, opposite the document title.
    if (section) {
      doc.setFontSize(14);
      doc.text(section.type.displayNamePlural, pageWidth - MARGIN, MARGIN + 10, { align: 'right' });
    }

    doc.setFontSize(10);
    doc.setFont('helvetica', 'normal');
    doc.text(
      `Period: ${format(startDate, DATE_FORMATS.DATE_MEDIUM)} - ${format(endDate, DATE_FORMATS.DATE_MEDIUM)}`,
      MARGIN,
      MARGIN + 17
    );
    doc.text(
      `Generated: ${format(new Date(), DATE_FORMATS.DATETIME_MEDIUM)}`,
      MARGIN,
      MARGIN + 22
    );

    // Statistics for this section, on the page it starts.
    if (section && isSectionStart) {
      const requestCount = new Set(
        section.rows.flatMap((row) => row.entries.map((e) => e.request.id)),
      ).size;
      doc.text(
        `Scheduled requests: ${requestCount} · Resources: ${section.rows.length} · Period: ${days} days`,
        pageWidth - MARGIN,
        MARGIN + 22,
        { align: 'right' },
      );
    }

    // Legend — horizontal strip between the header and the axis.
    doc.setFontSize(8);
    REQUEST_STATUS_ORDER.forEach((status, index) => {
      const x = MARGIN + index * 35;
      doc.setFillColor(...STATUS_RGB[status]);
      doc.roundedRect(x, 41, 5, 4, 0.5, 0.5, 'F');
      doc.setTextColor(0, 0, 0);
      doc.text(formatStatusLabel(status), x + 7, 44);
    });

    // Timeline axis + vertical grid lines, sized to this page's rows.
    const gridHeight = rowsOnPage * ROW_HEIGHT;
    const gridStep = Math.max(1, Math.floor(days / 10)); // Show ~10 grid lines

    for (let i = 0; i <= days; i += gridStep) {
      const date = new Date(startMs + i * 24 * 60 * 60 * 1000);
      const x = chartX + (i / days) * chartWidth;

      doc.setDrawColor(220, 220, 220);
      doc.line(x, CHART_Y, x, CHART_Y + gridHeight);

      doc.setTextColor(100, 100, 100);
      doc.text(format(date, DATE_FORMATS.DATE_HEADER), x, CHART_Y - 3, { align: 'center' });
    }

    // Footer
    doc.setFontSize(8);
    doc.setTextColor(150, 150, 150);
    doc.text('Orkyo', pageWidth / 2, pageHeight - 5, { align: 'center' });
    doc.text(`Page ${page} of ${totalPages}`, pageWidth - MARGIN, pageHeight - 5, { align: 'right' });
  };

  const drawRows = (rows: GanttRow[]) => {
    doc.setFontSize(8);

    rows.forEach((row, index) => {
      const y = CHART_Y + index * ROW_HEIGHT;

      // Resource label, right-aligned into the gutter
      doc.setFontSize(8);
      doc.setTextColor(0, 0, 0);
      doc.text(
        row.name,
        chartX - 3,
        y + BAR_HEIGHT / 2 + 2,
        { align: 'right', maxWidth: LABEL_GUTTER - 5 }
      );

      // One bar per assignment, clamped to the export window
      row.entries.forEach(({ request, assignment }) => {
        const barStartMs = Math.max(new Date(assignment.startUtc).getTime(), startMs);
        const barEndMs = Math.min(new Date(assignment.endUtc).getTime(), endMs);

        const barX = chartX + ((barStartMs - startMs) / timeRange) * chartWidth;
        // Floor at 0.8mm so a short booking stays visible at year scale.
        const barWidth = Math.max(((barEndMs - barStartMs) / timeRange) * chartWidth, 0.8);

        // Color based on status (shared STATUS_RGB source — see top of file)
        const color = STATUS_RGB[request.status] ?? [150, 150, 150];
        doc.setFillColor(...color);
        doc.setDrawColor(...color);
        doc.roundedRect(barX, y, barWidth, BAR_HEIGHT, 1, 1, 'F');

        // Add request name if bar is wide enough
        if (barWidth > 20) {
          doc.setTextColor(255, 255, 255);
          doc.setFontSize(7);
          doc.text(
            request.name,
            barX + 2,
            y + BAR_HEIGHT / 2 + 1.5,
            { maxWidth: barWidth - 4 }
          );
          doc.setFontSize(8);
        }
      });
    });
  };

  if (sections.length === 0) {
    // Nothing scheduled anywhere in the window — still produce the chrome so the
    // export is a readable "nothing here" rather than a failure.
    drawPageChrome(1, null, false, 0);
  } else {
    let page = 0;
    for (const section of sections) {
      const sectionPages = Math.ceil(section.rows.length / ROWS_PER_PAGE);
      for (let p = 0; p < sectionPages; p++) {
        page++;
        if (page > 1) doc.addPage();

        const pageRows = section.rows.slice(p * ROWS_PER_PAGE, (p + 1) * ROWS_PER_PAGE);
        drawPageChrome(page, section, p === 0, pageRows.length);
        drawRows(pageRows);
      }
    }
  }

  // Save the PDF. A single-type export says which type it covers.
  const scope = sections.length === 1 ? `${sections[0].type.key}-` : '';
  const pdfFilename =
    filename || `gantt-chart-${scope}${format(new Date(), DATE_FORMATS.DATE_ISO)}.pdf`;
  doc.save(pdfFilename);
}
