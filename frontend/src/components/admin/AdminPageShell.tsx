import type { ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { User, LogOut, Settings } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@foundation/src/components/ui/popover';
import { Separator } from '@foundation/src/components/ui/separator';
import { useAuth } from '@foundation/src/contexts/AuthContext';
import { ThemeToggle } from '@foundation/src/components/layout/ThemeToggle';
import { PageLayout } from '@foundation/src/components/layout/PageLayout';
import { PageHeader } from '@foundation/src/components/layout/PageHeader';

export interface AdminPageShellProps {
  /** Page title shown in the PageHeader (e.g. "Site Administration"). */
  title: string;
  /** Optional description rendered under the title, matching PageHeader chrome. */
  description?: string;
  /** Optional actions rendered in the PageHeader next to the title. */
  headerExtras?: ReactNode;
  /** When set, the user popover shows a "Manage Account" link pointing to this path. */
  accountHref?: string;
  /** Display name/email overrides; default to `appUser` from auth context. */
  displayName?: string;
  email?: string;
  /** The admin page body (tabs, content, etc). */
  children: ReactNode;
}

/**
 * Full-screen shell for the standalone admin surfaces (SaaS /site-admin,
 * Community /admin). It has no SidebarNav — it's a distinct route surface — but
 * it converges on the in-app PageLayout/PageHeader chrome so title/description
 * typography and page padding match the tenant admin. Because there's no TopBar
 * here, the slim header keeps the theme toggle and account popover.
 */
export function AdminPageShell({
  title,
  description,
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

        <div className="flex-1" />

        <div className="flex items-center gap-2">
          <ThemeToggle />

          <Popover>
            <PopoverTrigger asChild>
              <Button variant="ghost" size="icon" aria-label="Open account menu">
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

      <PageLayout>
        <PageHeader title={title} description={description} actions={headerExtras} />
        {children}
      </PageLayout>
    </div>
  );
}
