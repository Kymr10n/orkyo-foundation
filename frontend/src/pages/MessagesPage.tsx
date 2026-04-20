/**
 * Messages Page
 *
 * Standalone page for viewing platform announcements.
 */

import { useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { ChevronLeft } from "lucide-react";
import { MessagesTab } from "@/components/account/MessagesTab";

export function MessagesPage() {
  const navigate = useNavigate();

  return (
    <div className="container max-w-3xl py-8 space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="sm" onClick={() => navigate(-1)}>
          <ChevronLeft className="h-4 w-4 mr-1" />
          Back
        </Button>
        <div>
          <h1 className="text-2xl font-bold">Messages</h1>
          <p className="text-muted-foreground">Stay up to date with platform news</p>
        </div>
      </div>

      <MessagesTab />
    </div>
  );
}
