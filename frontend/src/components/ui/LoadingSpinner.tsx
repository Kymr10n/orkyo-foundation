import { Loader2 } from 'lucide-react';

interface LoadingSpinnerProps {
  message?: string;
  /**
   * Full viewport loader (default) for route/auth/Suspense boundaries, or a
   * contained loader that fills its parent region when `false` — use the latter
   * inside an in-page area (grid body, panel, table, settings section).
   */
  fullScreen?: boolean;
}

export function LoadingSpinner({ message, fullScreen = true }: LoadingSpinnerProps) {
  return (
    <div
      role="status"
      aria-busy="true"
      aria-live="polite"
      className={`flex items-center justify-center ${
        fullScreen ? "min-h-screen bg-background" : "h-full w-full"
      }`}
    >
      <div className="flex flex-col items-center gap-4">
        <Loader2 className="h-8 w-8 animate-spin text-primary" aria-hidden="true" />
        {/* Always give assistive tech a label; fall back to "Loading" when no
            visible message is provided. */}
        {message ? (
          <p className="text-muted-foreground text-sm">{message}</p>
        ) : (
          <span className="sr-only">Loading</span>
        )}
      </div>
    </div>
  );
}
