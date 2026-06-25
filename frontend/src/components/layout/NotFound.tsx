import { Link } from "react-router-dom";
import { Compass } from "lucide-react";
import { Button } from "@foundation/src/components/ui/button";

/**
 * Catch-all 404 page for unknown in-app URLs. Keeps the SPA from rendering
 * blank when a route does not match, and offers a way back to the home view.
 */
export function NotFound() {
  return (
    <div className="flex min-h-[60vh] items-center justify-center p-4">
      <div className="max-w-md space-y-4 rounded-lg border bg-card p-6 text-center">
        <div className="flex justify-center">
          <Compass className="h-12 w-12 text-muted-foreground" />
        </div>
        <div>
          <h1 className="text-lg font-semibold">Page not found</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            The page you are looking for doesn&apos;t exist or has moved.
          </p>
        </div>
        <Button asChild variant="outline" size="sm">
          <Link to="/">Go to home</Link>
        </Button>
      </div>
    </div>
  );
}
