import { Loader2 } from 'lucide-react';
import { cn } from '@foundation/src/lib/utils';

interface LoadingSpinnerProps {
  message?: string;
  /**
   * Full viewport loader (default) for route/auth/Suspense boundaries, or a
   * contained loader that fills its parent region when `false` — use the latter
   * inside an in-page area (grid body, panel, table, settings section).
   */
  fullScreen?: boolean;
  /** Icon size — `md` (route-level default) or `sm` for compact section loaders. */
  size?: 'md' | 'sm';
  /** Muted icon color for in-content section loaders (default is primary). */
  muted?: boolean;
  /**
   * Extra container classes (e.g. the section's fixed height or vertical padding).
   * Merged via `cn`, so height/width utilities here override the contained-mode
   * `h-full w-full` defaults.
   */
  className?: string;
}

export function LoadingSpinner({
  message,
  fullScreen = true,
  size = 'md',
  muted = false,
  className,
}: LoadingSpinnerProps) {
  return (
    <div
      role="status"
      aria-busy="true"
      aria-live="polite"
      className={cn(
        'flex items-center justify-center',
        fullScreen ? 'min-h-screen bg-background' : 'h-full w-full',
        className,
      )}
    >
      <div className="flex flex-col items-center gap-4">
        <Loader2
          className={cn(
            size === 'sm' ? 'h-6 w-6' : 'h-8 w-8',
            'animate-spin',
            muted ? 'text-muted-foreground' : 'text-primary',
          )}
          aria-hidden="true"
        />
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
