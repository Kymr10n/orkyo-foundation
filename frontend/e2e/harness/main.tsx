import { useState } from "react";
import { createRoot } from "react-dom/client";
import "./index.css";

import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { Input } from "@foundation/src/components/ui/input";
import { Button } from "@foundation/src/components/ui/button";
import {
  OrkyoDataTable,
  type ColumnDef,
} from "@foundation/src/components/ui/OrkyoDataTable";
import { TimelineGridShell } from "@foundation/src/components/utilization/TimelineGridShell";
import { PROBLEM_HATCH_CLASS } from "@foundation/src/components/utilization/schedule-colors";
import type { TimeColumn } from "@foundation/src/components/utilization/scheduler-types";
import { RequestCalendar } from "@foundation/src/components/utilization/RequestCalendar";
import type { CalendarEvent } from "@foundation/src/components/utilization/request-calendar-events";
import { ScheduleToDialog } from "@foundation/src/components/utilization/ScheduleToDialog";
import { makeRequest } from "@foundation/src/test-utils/request-fixtures";
import type { Space } from "@foundation/src/types/space";

// ── Fixtures ──────────────────────────────────────────────────────────────────

interface TableRow {
  id: string;
  name: string;
}
const tableRows: TableRow[] = Array.from({ length: 5 }, (_, i) => ({
  id: `row-${i + 1}`,
  name: `Item ${i + 1}`,
}));
const tableColumns: ColumnDef<TableRow>[] = [
  { accessorKey: "name", header: "Name" },
];

function makeColumn(hour: string): TimeColumn {
  const start = new Date(`2026-04-17T${hour}:00:00Z`);
  const end = new Date(start.getTime() + 60 * 60 * 1000);
  return { start, end, label: `${hour}:00` };
}
const gridColumns = [makeColumn("08"), makeColumn("09"), makeColumn("10")];
const gridGroups = [
  { id: "g1", name: "Crew A", rows: [{ id: "r1", name: "Alice" }] },
];

const calendarEvent: CalendarEvent = {
  id: "r1",
  title: "Fixture Event",
  start: "2026-04-17T09:00:00Z",
  end: "2026-04-17T11:00:00Z",
  classNames: [],
  editable: true,
  extendedProps: { requestId: "r1", status: "new", conflictSeverity: null },
};

function makeSpace(id: string, name: string): Space {
  return {
    id,
    siteId: "site-1",
    name,
    isPhysical: true,
    capacity: 1,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
  };
}
const spaces = [makeSpace("space-1", "Room A"), makeSpace("space-2", "Room B")];

// ── Harness page ──────────────────────────────────────────────────────────────

function Harness() {
  const [isDark, setIsDark] = useState(false);

  const [formOpen, setFormOpen] = useState(false);
  const [taskName, setTaskName] = useState("");
  const [submitted, setSubmitted] = useState<string | null>(null);

  const [scheduleOpen, setScheduleOpen] = useState(false);

  const toggleTheme = () => {
    setIsDark((prev) => {
      const next = !prev;
      document.documentElement.classList.toggle("dark", next);
      return next;
    });
  };

  return (
    <div className="p-6 space-y-8 max-w-4xl mx-auto">
      <header className="flex items-center justify-between">
        <h1 className="text-lg font-semibold">Foundation smoke harness</h1>
        <Button
          data-testid="theme-toggle"
          variant="outline"
          onClick={toggleTheme}
        >
          {isDark ? "Light" : "Dark"} theme
        </Button>
      </header>

      {/* FormDialog ------------------------------------------------------------ */}
      <section className="space-y-2">
        <h2 className="font-medium">FormDialog</h2>
        <Button data-testid="open-form" onClick={() => setFormOpen(true)}>
          Open form
        </Button>
        {submitted !== null && (
          <p data-testid="form-result">Submitted: {submitted}</p>
        )}
        <FormDialog
          open={formOpen}
          onOpenChange={setFormOpen}
          title="Create task"
          description="Give the task a name, then submit."
          onSubmit={() => {
            setSubmitted(taskName);
            setFormOpen(false);
          }}
          isSubmitting={false}
          submitLabel="Save"
        >
          <Input
            aria-label="task-name"
            data-testid="task-name"
            value={taskName}
            onChange={(e) => setTaskName(e.target.value)}
          />
        </FormDialog>
      </section>

      {/* OrkyoDataTable -------------------------------------------------------- */}
      <section className="space-y-2">
        <h2 className="font-medium">OrkyoDataTable</h2>
        <div data-testid="data-table">
          <OrkyoDataTable columns={tableColumns} data={tableRows} pageSize={2} />
        </div>
      </section>

      {/* Utilization grid + off-time hatch ------------------------------------- */}
      <section className="space-y-2">
        <h2 className="font-medium">Utilization grid</h2>
        <div className="h-64 border rounded-lg overflow-hidden">
          <TimelineGridShell
            labelHeader="Crew"
            columns={gridColumns}
            scale="hour"
            groups={gridGroups}
            collapseIdPrefix="harness"
            getRowId={(r) => r.id}
            emptyMessage="No rows."
            renderRow={(r) => (
              <div className="flex h-[60px]" data-testid={`row-${r.id}`}>
                <div className="w-52 flex-shrink-0 px-3 py-2 border-r text-sm">
                  {r.name}
                </div>
                {gridColumns.map((col, i) => (
                  <div
                    key={col.start.getTime()}
                    // First column carries the off-time hatch so the smoke test
                    // can assert the repeating-linear-gradient renders.
                    data-testid={i === 0 ? "offtime-cell" : undefined}
                    className={`flex-1 min-w-[60px] border-r ${
                      i === 0 ? PROBLEM_HATCH_CLASS : ""
                    }`}
                  />
                ))}
              </div>
            )}
          />
        </div>
      </section>

      {/* RequestCalendar ------------------------------------------------------- */}
      <section className="space-y-2">
        <h2 className="font-medium">RequestCalendar</h2>
        <div className="h-96 border rounded-lg overflow-hidden" data-testid="calendar">
          <RequestCalendar
            events={[calendarEvent]}
            editable
            initialView="timeGridWeek"
            initialDate={new Date("2026-04-17T00:00:00Z")}
            onEventClick={() => {}}
            onEventMove={() => {}}
            onEventResize={() => {}}
            onSlotSelect={() => {}}
            onDatesSet={() => {}}
          />
        </div>
      </section>

      {/* ScheduleToDialog ------------------------------------------------------ */}
      <section className="space-y-2">
        <h2 className="font-medium">ScheduleToDialog</h2>
        <Button data-testid="open-schedule" onClick={() => setScheduleOpen(true)}>
          Open schedule
        </Button>
        <ScheduleToDialog
          open={scheduleOpen}
          onOpenChange={setScheduleOpen}
          request={makeRequest({ id: "u-1", name: "Backlog Task" })}
          spaces={spaces}
          onSchedule={async () => {}}
          defaultStart={new Date(2026, 3, 17, 9, 0)}
        />
      </section>
    </div>
  );
}

createRoot(document.getElementById("root")!).render(<Harness />);
