/**
 * MessagesTab – User-facing tab showing platform announcements.
 *
 * Displays active announcements with read/unread state.
 * Users can expand announcements to read the body and mark them as read.
 */

import { useState } from 'react';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@foundation/src/components/ui/card';
import { Badge } from '@foundation/src/components/ui/badge';
import { Button } from '@foundation/src/components/ui/button';
import { Alert, AlertDescription } from '@foundation/src/components/ui/alert';
import { EmptyState } from '@foundation/src/components/ui/EmptyState';
import { Megaphone, AlertCircle, ChevronDown, ChevronUp, CheckCircle2, Circle, AlertTriangle } from 'lucide-react';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import { type UserAnnouncement } from '@foundation/src/lib/api/user-announcements-api';
import {
  useActiveAnnouncements,
  useMarkAnnouncementRead,
} from '@foundation/src/hooks/useMessages';
import { formatDistanceToNow } from 'date-fns';

export function MessagesTab() {
  const [expandedId, setExpandedId] = useState<string | null>(null);

  const { data, isLoading, error } = useActiveAnnouncements();

  const announcements = data ?? [];

  const markReadMutation = useMarkAnnouncementRead();

  const handleToggle = (announcement: UserAnnouncement) => {
    const isExpanding = expandedId !== announcement.id;
    setExpandedId(isExpanding ? announcement.id : null);

    if (isExpanding && !announcement.isRead) {
      markReadMutation.mutate(announcement.id);
    }
  };

  const unreadCount = announcements.filter((a) => !a.isRead).length;

  if (isLoading) {
    return (
      <div className="py-12">
        <LoadingSpinner fullScreen={false} message="Loading messages…" />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {error && (
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertDescription>{error.message}</AlertDescription>
        </Alert>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Megaphone className="h-5 w-5" />
            Messages
            {unreadCount > 0 && (
              <Badge variant="default" className="ml-2">
                {unreadCount} unread
              </Badge>
            )}
          </CardTitle>
          <CardDescription>
            Platform announcements from the Orkyo team
          </CardDescription>
        </CardHeader>
        <CardContent>
          {announcements.length === 0 ? (
            <EmptyState
              icon={<Megaphone className="h-8 w-8 mx-auto mb-3 opacity-40" />}
              message={<p>No messages at this time.</p>}
            />
          ) : (
            <div className="space-y-2">
              {announcements.map((a) => {
                const isExpanded = expandedId === a.id;
                return (
                  <div
                    key={a.id}
                    className={`rounded-lg border transition-colors ${
                      !a.isRead
                        ? 'border-primary/40 bg-primary/5'
                        : 'border-border'
                    }`}
                  >
                    <Button
                      variant="ghost"
                      className="w-full justify-start px-4 py-3 h-auto text-left"
                      onClick={() => handleToggle(a)}
                    >
                      <div className="flex items-start gap-3 w-full">
                        <div className="mt-0.5 flex-shrink-0">
                          {a.isRead ? (
                            <CheckCircle2 className="h-4 w-4 text-muted-foreground" />
                          ) : (markReadMutation.isPending && markReadMutation.variables === a.id) ? (
                            <LoadingSpinner inline size="xs" />
                          ) : (
                            <Circle className="h-4 w-4 text-primary fill-primary" />
                          )}
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2">
                            <span className={`font-medium truncate ${!a.isRead ? 'text-foreground' : 'text-muted-foreground'}`}>
                              {a.title}
                            </span>
                            {a.isImportant && (
                              <AlertTriangle className="h-3.5 w-3.5 text-amber-500 flex-shrink-0" />
                            )}
                          </div>
                          <span className="text-xs text-muted-foreground">
                            {formatDistanceToNow(new Date(a.createdAt), { addSuffix: true })}
                          </span>
                        </div>
                        <div className="flex-shrink-0 mt-0.5">
                          {isExpanded ? (
                            <ChevronUp className="h-4 w-4 text-muted-foreground" />
                          ) : (
                            <ChevronDown className="h-4 w-4 text-muted-foreground" />
                          )}
                        </div>
                      </div>
                    </Button>

                    {isExpanded && (
                      <div className="px-4 pb-4 pl-11">
                        <div className="text-sm text-foreground whitespace-pre-wrap">
                          {a.body}
                        </div>
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
