/**
 * Utilization Gantt chart, printed through the browser.
 *
 * Renders the chart into a print-only root and calls `window.print()`; the person saves it
 * as a PDF from the print dialog. This replaced a jsPDF drawing (2026-10 supply-chain pass):
 * jsPDF pulled canvg, html2canvas, dompurify and core-js — the only package with an install
 * script in the production bundles — into every product for this one feature. The browser's
 * own paginator does the page breaks, repeats the axis header on every page (table `thead`)
 * and numbers the pages (`@page` margin boxes, where the engine supports them).
 *
 * Reached through the dynamic import() in export-handlers.ts so the print markup and
 * styles stay out of the main chunk.
 */
import { flushSync } from 'react-dom';
import { createRoot, type Root } from 'react-dom/client';
import type { Request, RequestStatus, ResourceAssignment } from '@foundation/src/types/requests';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import { format } from 'date-fns';
import { DATE_FORMATS } from '@foundation/src/lib/formatters';
import { REQUEST_STATUS_ORDER } from '@foundation/src/constants/request-status';
import { formatStatusLabel } from '@foundation/src/lib/utils/utils';

/** Single status→colour source for both the bars and the legend, keyed over every status so a
 *  new/renamed status can't render unmapped or drop out of the legend. Order follows the canonical
 *  REQUEST_STATUS_ORDER; labels come from the shared formatStatusLabel. */
export const STATUS_COLOR: Record<RequestStatus, string> = {
  new: '#3b82f6',          // blue
  in_progress: '#f97316',  // orange
  done: '#22c55e',         // green
  deferred: '#64748b',     // slate
  cancelled: '#9ca3af',    // gray
};
const UNKNOWN_STATUS_COLOR = '#969696';

/** The id of the print root; the stylesheet keys off it. */
export const PRINT_ROOT_ID = 'orkyo-gantt-print';

/** A bar narrower than this shows no label — the text would be clipped to nothing. */
const LABEL_MIN_WIDTH_PCT = 8;
/** Floor so a short booking stays visible at year scale. */
const BAR_MIN_WIDTH_PCT = 0.3;

const DAY_MS = 24 * 60 * 60 * 1000;

/** Non-cancelled assignments — the ones that actually occupy a resource. */
function liveAssignments(request: Request) {
  return (request.assignments ?? []).filter((a) => a.assignmentStatus !== 'Cancelled');
}

/** What the chart knows about a resource: its label and which type's section it belongs in. */
export interface GanttResource {
  name: string;
  typeKey: string;
}

export interface GanttPrintOptions {
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
}

/** One resource's row within a section. */
interface GanttRow {
  resourceId: string;
  name: string;
  entries: { request: Request; assignment: ResourceAssignment }[];
}

interface GanttSection {
  type: ResourceTypeInfo;
  rows: GanttRow[];
}

/**
 * The sections of the chart: one per type that has something scheduled in the window, in
 * the order given. Rows are one per resource, one bar per assignment — the same shape the
 * on-screen grid renders (a request occupying a room for one day of its week shows a
 * one-day bar on that room's row). A request on a space AND a person appears in both
 * sections — that is what it does to those resources. Only assignments overlapping the
 * window count; the caller hands us its whole buffered fetch window, most of which lies
 * outside the visible period.
 */
function buildSections(options: GanttPrintOptions): GanttSection[] {
  const { requests, resources, resourceTypes, startDate, endDate } = options;
  const startMs = startDate.getTime();
  const endMs = endDate.getTime();

  // Scheduled = has a time window and at least one live assignment. This used
  // to require a SPACE assignment, so a request booked onto a person or a tool
  // vanished from the chart — an empty export on any tenant scheduling both.
  const scheduledRequests = requests.filter(
    (r) => r.startTs && r.endTs && liveAssignments(r).length > 0,
  );

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
  return resourceTypes
    .map((type) => ({ type, rows: rowsForType(type.key) }))
    .filter((section) => section.rows.length > 0);
}

/** ~10 evenly spaced day ticks across the window, as percentages of the timeline width. */
function axisTicks(startMs: number, days: number): { pct: number; label: string }[] {
  const step = Math.max(1, Math.floor(days / 10));
  const ticks: { pct: number; label: string }[] = [];
  for (let i = 0; i <= days; i += step) {
    ticks.push({ pct: (i / days) * 100, label: format(new Date(startMs + i * DAY_MS), DATE_FORMATS.DATE_HEADER) });
  }
  return ticks;
}

// Everything the print needs lives in this one stylesheet, injected with the root: products
// import no foundation CSS, and the chart must look the same in all of them. On screen the
// root is hidden; in print it is the only thing on the page.
const PRINT_CSS = `
#${PRINT_ROOT_ID} { display: none; }
@media print {
  @page {
    size: A4 landscape;
    margin: 12mm;
    @bottom-center { content: "Orkyo"; font: 8pt Helvetica, Arial, sans-serif; color: #969696; }
    @bottom-right { content: "Page " counter(page) " of " counter(pages); font: 8pt Helvetica, Arial, sans-serif; color: #969696; }
  }
  body > *:not(#${PRINT_ROOT_ID}) { display: none !important; }
  #${PRINT_ROOT_ID} {
    display: block;
    font: 9pt/1.3 Helvetica, Arial, sans-serif;
    color: #000;
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }
  .gp-section { break-before: page; }
  .gp-section:first-child { break-before: auto; }
  .gp-header { display: flex; justify-content: space-between; align-items: baseline; }
  .gp-title { font-size: 16pt; font-weight: 700; margin: 0; }
  .gp-type { font-size: 13pt; font-weight: 700; }
  .gp-meta { display: flex; justify-content: space-between; margin: 1.5mm 0; font-size: 9pt; }
  .gp-legend { display: flex; gap: 6mm; margin: 2mm 0 3mm; padding: 0; list-style: none; font-size: 8pt; }
  .gp-swatch { display: inline-block; width: 4mm; height: 3mm; border-radius: 0.5mm; margin-right: 1.5mm; vertical-align: middle; }
  .gp-table { width: 100%; border-collapse: collapse; table-layout: fixed; }
  .gp-table thead { display: table-header-group; }
  .gp-table th, .gp-table td { padding: 0; vertical-align: middle; }
  .gp-label-head, .gp-label {
    width: 45mm; text-align: right; padding-right: 2mm; font-weight: 400; font-size: 8pt;
    overflow: hidden; white-space: nowrap; text-overflow: ellipsis;
  }
  .gp-table tbody tr { break-inside: avoid; height: 7mm; }
  .gp-axis { position: relative; height: 6mm; font-size: 7pt; color: #646464; }
  .gp-axis span { position: absolute; bottom: 0; transform: translateX(-50%); white-space: nowrap; }
  .gp-timeline { position: relative; height: 7mm; }
  .gp-grid { position: absolute; top: 0; bottom: 0; border-left: 0.2mm solid #dcdcdc; }
  .gp-bar {
    position: absolute; top: 1mm; height: 5mm; border-radius: 1mm; box-sizing: border-box;
    padding: 0 1mm; overflow: hidden; white-space: nowrap;
    color: #fff; font-size: 7pt; line-height: 5mm;
  }
  .gp-empty { margin-top: 10mm; color: #646464; }
}
`;

function GanttPrintDocument({ options, generatedAt }: { options: GanttPrintOptions; generatedAt: Date }) {
  const { startDate, endDate } = options;
  const startMs = startDate.getTime();
  const endMs = endDate.getTime();
  const timeRange = endMs - startMs;
  const days = Math.ceil(timeRange / DAY_MS);
  const sections = buildSections(options);
  const ticks = axisTicks(startMs, days);

  const header = (section: GanttSection | null) => {
    const requestCount = section
      ? new Set(section.rows.flatMap((row) => row.entries.map((e) => e.request.id))).size
      : 0;
    return (
      <>
        <div className="gp-header">
          <h1 className="gp-title">Utilization Gantt Chart</h1>
          {section && <span className="gp-type">{section.type.displayNamePlural}</span>}
        </div>
        <div className="gp-meta">
          <span>
            Period: {format(startDate, DATE_FORMATS.DATE_MEDIUM)} - {format(endDate, DATE_FORMATS.DATE_MEDIUM)}
          </span>
          {section && (
            <span className="gp-stats">
              Scheduled requests: {requestCount} · Resources: {section.rows.length} · Period: {days} days
            </span>
          )}
        </div>
        <div className="gp-meta">
          <span>Generated: {format(generatedAt, DATE_FORMATS.DATETIME_MEDIUM)}</span>
        </div>
        <ul className="gp-legend">
          {REQUEST_STATUS_ORDER.map((status) => (
            <li key={status}>
              <span className="gp-swatch" style={{ background: STATUS_COLOR[status] }} />
              {formatStatusLabel(status)}
            </li>
          ))}
        </ul>
      </>
    );
  };

  const bar = ({ request, assignment }: GanttRow['entries'][number]) => {
    // Clamp to the window; a bar straddling the edge starts at the chart edge.
    const barStartMs = Math.max(new Date(assignment.startUtc).getTime(), startMs);
    const barEndMs = Math.min(new Date(assignment.endUtc).getTime(), endMs);
    const left = ((barStartMs - startMs) / timeRange) * 100;
    const width = Math.max(((barEndMs - barStartMs) / timeRange) * 100, BAR_MIN_WIDTH_PCT);
    return (
      <span
        key={assignment.id}
        className="gp-bar"
        data-request-id={request.id}
        data-status={request.status}
        title={request.name}
        style={{ left: `${left}%`, width: `${width}%`, background: STATUS_COLOR[request.status] ?? UNKNOWN_STATUS_COLOR }}
      >
        {width >= LABEL_MIN_WIDTH_PCT ? request.name : ''}
      </span>
    );
  };

  return (
    <>
      <style>{PRINT_CSS}</style>
      {sections.length === 0 ? (
        // Nothing scheduled anywhere in the window — still produce the chrome so the
        // print is a readable "nothing here" rather than a blank page.
        <section className="gp-section">
          {header(null)}
          <p className="gp-empty">Nothing is scheduled in this period.</p>
        </section>
      ) : (
        sections.map((section) => (
          <section key={section.type.key} className="gp-section" data-type-key={section.type.key}>
            {header(section)}
            <table className="gp-table">
              <thead>
                <tr>
                  <th className="gp-label-head" />
                  <th>
                    <div className="gp-axis">
                      {ticks.map((tick) => (
                        <span key={tick.pct} style={{ left: `${tick.pct}%` }}>{tick.label}</span>
                      ))}
                    </div>
                  </th>
                </tr>
              </thead>
              <tbody>
                {section.rows.map((row) => (
                  <tr key={row.resourceId} data-resource-id={row.resourceId}>
                    <th scope="row" className="gp-label">{row.name}</th>
                    <td>
                      <div className="gp-timeline">
                        {ticks.map((tick) => (
                          <span key={tick.pct} className="gp-grid" style={{ left: `${tick.pct}%` }} />
                        ))}
                        {row.entries.map(bar)}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        ))
      )}
    </>
  );
}

let active: { container: HTMLElement; cleanup: () => void } | null = null;

/**
 * Render the chart into a print-only root and open the browser's print dialog. The root is
 * removed after printing (`afterprint`); a second call before that replaces it.
 */
export function printGanttChart(options: GanttPrintOptions): void {
  active?.cleanup();

  const container = document.createElement('div');
  container.id = PRINT_ROOT_ID;
  document.body.appendChild(container);
  const root: Root = createRoot(container);

  const cleanup = () => {
    window.removeEventListener('afterprint', cleanup);
    root.unmount();
    container.remove();
    if (active?.container === container) active = null;
  };
  active = { container, cleanup };

  // Synchronous render: the markup must exist before print() snapshots the page.
  flushSync(() => root.render(<GanttPrintDocument options={options} generatedAt={new Date()} />));
  window.addEventListener('afterprint', cleanup);
  window.print();
}
