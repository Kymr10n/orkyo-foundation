import type { ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { Shield, User, LogOut, Settings } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@foundation/src/components/ui/popover';
import { Separator } from '@foundation/src/components/ui/separator';
import { useAuth } from '@foundation/src/contexts/AuthContext';
import { ThemeToggle } from '@foundation/src/components/layout/ThemeToggle';

export interface AdminPageShellProps {
  /** Breadcrumb label shown next to the shield icon (e.g. "Site Administration"). */
  breadcrumbLabel: string;
  /** Optional content rendered between the breadcrumb and the theme toggle. */
  headerExtras?: ReactNode;
  /** When set, the user popover shows a "Manage Account" link pointing to this path. */
  accountHref?: string;
  /** Display name/email overrides; default to `appUser` from auth context. */
  displayName?: string;
  email?: string;
  /** The admin page body (tabs, content, etc). */
  children: ReactNode;
}

export function AdminPageShell({
  breadcrumbLabel,
  headerExtras,
  accountHref,
  displayName,
  email,
  children,
}: AdminPageShellProps) {
  const { appUser, logout } = useAuth();
  const navigate = useNavigate();

  // Use ||, not ??, so empty strings fall back too — matches the original
  // SaaS/Community AdminPage behaviour where "" displayName showed "Admin".
  const name = displayName || appUser?.displayName || 'Admin';
  const userEmail = email || appUser?.email;

  return (
    <div className="min-h-screen bg-background">
      <header className="h-14 border-b bg-card flex items-center px-4 gap-4 sticky top-0 z-50">
        <div className="font-semibold text-base whitespace-nowrap">Orkyo</div>
        <div className="flex items-center gap-2 text-muted-foreground">
          <span className="text-sm">/</span>
          <Shield className="h-4 w-4" />
          <span className="text-sm font-medium">{breadcrumbLabel}</span>
        </div>

        <div className="flex-1" />

        <div className="flex items-center gap-2">
          {headerExtras}
          <ThemeToggle />

          <Popover>
            <PopoverTrigger asChild>
              <Button variant="ghost" size="icon">
                <User className="h-4 w-4" />
              </Button>
            </PopoverTrigger>
            <PopoverContent align="end" className="w-64">
              <div className="space-y-3">
                <div className="space-y-1">
                  <p className="text-sm font-medium">{name}</p>
                  <p className="text-xs text-muted-foreground">{userEmail}</p>
                </div>
                <Separator />
                {accountHref && (
                  <Button
                    variant="ghost"
                    className="w-full justify-start h-9"
                    onClick={() => navigate(accountHref)}
                  >
                    <Settings className="h-4 w-4 mr-2" />
                    Manage Account
                  </Button>
                )}
                <Button
                  variant="ghost"
                  className="w-full justify-start h-9 text-destructive hover:text-destructive"
                  onClick={logout}
                >
                  <LogOut className="h-4 w-4 mr-2" />
                  Sign out
                </Button>
              </div>
            </PopoverContent>
          </Popover>
        </div>
      </header>

      <div className="container mx-auto px-6 py-6">{children}</div>
    </div>
  );
}
