import { Component, type ErrorInfo, type ReactNode } from 'react';
import { AlertCircle, RotateCcw } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { logger } from '@foundation/src/lib/core/logger';

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

    if (this.props.fallback) return this.props.fallback(error, this.reset);

    return (
      <div className="flex items-center justify-center py-16">
        <div className="max-w-md space-y-4 rounded-lg border bg-card p-6 text-center">
          <div className="flex justify-center">
            <AlertCircle className="h-12 w-12 text-destructive" />
          </div>
          <div>
            <h2 className="text-lg font-semibold">Something went wrong</h2>
            <p className="mt-1 text-sm text-muted-foreground">
              {this.props.label ? `The ${this.props.label} view ` : 'This view '}
              failed to render. The rest of the app is unaffected.
            </p>
          </div>
          <p className="font-mono text-xs text-muted-foreground break-words">
            {error.message}
          </p>
          <Button variant="outline" size="sm" onClick={this.reset}>
            <RotateCcw className="h-4 w-4 mr-2" />
            Try again
          </Button>
        </div>
      </div>
    );
  }
}
