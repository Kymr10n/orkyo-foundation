import { useNavigate, useParams } from "react-router";
import { PageLayout } from "@foundation/src/components/layout/PageLayout";
import { RequestPlanPanel } from "@foundation/src/components/requests/plan/RequestPlanPanel";

/**
 * Route wrapper for the dependency planner.
 *
 * A route rather than a dialog: the widest sanctioned dialog is max-w-3xl, which is too narrow
 * for a graph of any size. The floorplan editor sets the same precedent.
 */
export function RequestPlanPage() {
  const { requestId } = useParams<{ requestId: string }>();
  const navigate = useNavigate();

  if (!requestId) return null;

  return (
    <PageLayout>
      <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-xl border bg-background">
        <RequestPlanPanel
          requestId={requestId}
          // Opening a task from the planner reuses the request list's deep link, so the editor
          // that opens is the same one every other surface opens.
          onOpenRequest={(id) => navigate(`/requests?edit=${id}`)}
        />
      </div>
    </PageLayout>
  );
}
