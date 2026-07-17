import { Component, type ErrorInfo, type ReactNode } from 'react';
import { AlertCircle, RefreshCw, RotateCcw } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { logger } from '@foundation/src/lib/core/logger';
import { isStaleChunkError } from '@foundation/src/lib/core/stale-chunk';

interface RouteErrorBoundaryProps {
  /** Subtree to protect. */
  children: ReactNode;
  /** Optional friendly label shown in the fallback (e.g. "Admin", "Tenants tab"). */
  label?: string;
  /** Optional custom fallback renderer. Falls back to the built-in card. */
  fallback?: (error: Error, reset: () => void) => ReactNode;
}

interface RouteErrorBoundaryState {
  error: Error | null;
}

/** Shared card shell for both fallbacks — keeps the stale-chunk and generic markup identical. */
function FallbackCard({
  icon,
  title,
  body,
  detail,
  action,
}: {
  icon: ReactNode;
  title: string;
  body: ReactNode;
  /** Optional monospace detail line (e.g. the error message). */
  detail?: ReactNode;
  action: ReactNode;
}) {
  return (
    <div className="flex items-center justify-center py-16">
      <div className="max-w-md space-y-4 rounded-lg border bg-card p-6 text-center">
        <div className="flex justify-center">{icon}</div>
        <div>
          <h2 className="text-lg font-semibold">{title}</h2>
          <p className="mt-1 text-sm text-muted-foreground">{body}</p>
        </div>
        {detail && (
          <p className="font-mono text-xs text-muted-foreground break-words">{detail}</p>
        )}
        {action}
      </div>
    </div>
  );
}

/**
 * Catches render-time exceptions in a route subtree and shows a recoverable
 * fallback instead of crashing the whole app. Mount around route outlets,
 * tab content, or any subtree that can fail independently.
 */
export class RouteErrorBoundary extends Component<RouteErrorBoundaryProps, RouteErrorBoundaryState> {
  state: RouteErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): RouteErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    logger.error(`RouteErrorBoundary${this.props.label ? ` [${this.props.label}]` : ''}:`, error, info.componentStack);
  }

  private reset = () => {
    this.setState({ error: null });
  };

  render() {
    const { error } = this.state;
    if (!error) return this.props.children;

    // A failed chunk import means the deployment changed under this tab; a
    // state reset re-requests the same dead URL, so offer a reload instead.
    // Takes precedence over custom fallbacks — they can't recover from this.
    if (isStaleChunkError(error)) {
      return (
        <FallbackCard
          icon={<RefreshCw className="h-12 w-12 text-muted-foreground" />}
          title="A new version is available"
          body={
            <>
              Orkyo has been updated since this page was loaded. Reload to
              continue where you left off.
            </>
          }
          action={
            <Button variant="outline" size="sm" onClick={() => window.location.reload()}>
              <RotateCcw className="h-4 w-4 mr-2" />
              Reload
            </Button>
          }
        />
      );
    }

    if (this.props.fallback) return this.props.fallback(error, this.reset);

    return (
      <FallbackCard
        icon={<AlertCircle className="h-12 w-12 text-destructive" />}
        title="Something went wrong"
        body={
          <>
            {this.props.label ? `The ${this.props.label} view ` : 'This view '}
            failed to render. The rest of the app is unaffected.
          </>
        }
        detail={error.message}
        action={
          <Button variant="outline" size="sm" onClick={this.reset}>
            <RotateCcw className="h-4 w-4 mr-2" />
            Try again
          </Button>
        }
      />
    );
  }
}
