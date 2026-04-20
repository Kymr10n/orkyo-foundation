import { AlertTriangle, WifiOff, RefreshCw } from 'lucide-react';
import { Button } from '@/components/ui/button';

interface AuthErrorScreenProps {
  variant: 'backend' | 'network';
  title: string;
  detail: string;
  onRetry: () => void;
}

export function AuthErrorScreen({ variant, title, detail, onRetry }: AuthErrorScreenProps) {
  const Icon = variant === 'network' ? WifiOff : AlertTriangle;

  return (
    <div className="flex items-center justify-center min-h-screen bg-background">
      <div className="flex flex-col items-center gap-4 text-center max-w-md px-4">
        <Icon className="h-12 w-12 text-destructive" />
        <h1 className="text-xl font-semibold">{title}</h1>
        <p className="text-muted-foreground text-sm">{detail}</p>
        <Button onClick={onRetry}>
          <RefreshCw className="mr-2 h-4 w-4" />
          Try again
        </Button>
      </div>
    </div>
  );
}
