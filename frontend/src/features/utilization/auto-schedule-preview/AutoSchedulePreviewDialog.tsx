import {
  formatSolverKind,
  SchedulingReasonLabels,
  type AutoSchedulePreviewResponse,
} from "../../../../contracts/autoSchedule";

interface Props {
  open: boolean;
  preview: AutoSchedulePreviewResponse | null;
  isApplying?: boolean;
  applyError?: string | null;
  onApply: () => void;
  onClose: () => void;
}

export function AutoSchedulePreviewDialog({
  open,
  preview,
  isApplying,
  applyError,
  onApply,
  onClose,
}: Props) {
  if (!open) {
    return null;
  }

  return (
    <section aria-label="Auto-schedule preview dialog" className="shell">
      <header>
        <h2>Auto-schedule preview</h2>
        <p>Review the proposed placements before committing them.</p>
      </header>

      {!preview ? (
        <div aria-label="Loading preview">Loading...</div>
      ) : (
        <div>
          <section aria-label="Summary cards">
            <div>
              <div>Solver</div>
              <div>{formatSolverKind(preview.solverUsed)}</div>
            </div>
            <div>
              <div>Scheduled</div>
              <div>{preview.score.scheduledCount}</div>
            </div>
            <div>
              <div>Unscheduled</div>
              <div>{preview.score.unscheduledCount}</div>
            </div>
          </section>

          <section>
            <h3>Assignments ({preview.assignments.length})</h3>
            <div>
              {preview.assignments.length === 0 ? (
                <div>No assignments proposed.</div>
              ) : (
                <table>
                  <thead>
                    <tr>
                      <th>Request</th>
                      <th>Space</th>
                      <th>Start</th>
                      <th>End</th>
                      <th>Days</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.assignments.map((a) => (
                      <tr key={a.requestId}>
                        <td>{a.requestName}</td>
                        <td>{a.spaceName}</td>
                        <td>{a.start}</td>
                        <td>{a.end}</td>
                        <td>{a.durationDays}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </section>

          {preview.unscheduled.length > 0 && (
            <section>
              <h3>Unscheduled ({preview.unscheduled.length})</h3>
              <ul>
                {preview.unscheduled.map((entry) => (
                  <li key={entry.requestId}>
                    <span>{entry.requestName}</span>: {" "}
                    {entry.reasonCodes
                      .map((c) => SchedulingReasonLabels[c] ?? c)
                      .join(", ")}
                  </li>
                ))}
              </ul>
            </section>
          )}

          {preview.diagnostics.length > 0 && (
            <section>
              <h3>Diagnostics</h3>
              <ul>
                {preview.diagnostics.map((line, idx) => (
                  <li key={idx}>{line}</li>
                ))}
              </ul>
            </section>
          )}
        </div>
      )}

      {applyError && (
        <div role="alert">
          <span>{applyError}</span>
        </div>
      )}

      <footer>
        <button type="button" onClick={onClose}>
          Cancel
        </button>
        <button
          type="button"
          onClick={onApply}
          disabled={!preview || preview.assignments.length === 0 || isApplying}
        >
          Apply {preview ? `(${preview.assignments.length})` : ""}
        </button>
      </footer>
    </section>
  );
}
