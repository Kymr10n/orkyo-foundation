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
  /** Icon size — `md` (route-level default), `sm` for section loaders, `xs` for an inline one. */
  size?: 'md' | 'sm' | 'xs';
  /**
   * Inline presentation: icon and message on one row, no centring box, so the
   * spinner can sit in a header, a toolbar, a list row or a status line. The
   * default is the centred block that fills its region.
   */
  inline?: boolean;
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
  inline = false,
  className,
}: LoadingSpinnerProps) {
  const iconSize = size === 'xs' ? 'h-3.5 w-3.5' : size === 'sm' ? 'h-6 w-6' : 'h-8 w-8';
  return (
    <div
      role="status"
      aria-busy="true"
      aria-live="polite"
      className={cn(
        'flex items-center justify-center',
        inline ? 'inline-flex' : fullScreen ? 'min-h-screen bg-background' : 'h-full w-full',
        className,
      )}
    >
      <div className={cn('flex items-center', inline ? 'flex-row gap-2' : 'flex-col gap-4')}>
        <Loader2
          className={cn(
            iconSize,
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
