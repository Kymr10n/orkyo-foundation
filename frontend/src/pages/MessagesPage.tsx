/**
 * Messages Page
 *
 * Standalone page for viewing platform announcements.
 */

import { useNavigate } from "react-router-dom";
import { Button } from "@foundation/src/components/ui/button";
import { ChevronLeft } from "lucide-react";
import { FocusedPageLayout } from "@foundation/src/components/layout/FocusedPageLayout";
import { PageHeader } from "@foundation/src/components/layout/PageHeader";
import { usePageTitle } from "@foundation/src/hooks/usePageTitle";
import { MessagesTab } from "@foundation/src/components/account/MessagesTab";

export function MessagesPage() {
  usePageTitle("Messages");
  const navigate = useNavigate();

  return (
    <FocusedPageLayout>
      <PageHeader
        title="Messages"
        description="Stay up to date with platform news"
        actions={
          <Button variant="ghost" size="sm" onClick={() => navigate(-1)}>
            <ChevronLeft className="h-4 w-4 mr-1" />
            Back
          </Button>
        }
      />

      <MessagesTab />
    </FocusedPageLayout>
  );
}
