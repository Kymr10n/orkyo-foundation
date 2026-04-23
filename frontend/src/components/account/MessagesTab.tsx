/**
 * MessagesTab – User-facing tab showing platform announcements.
 *
 * Displays active announcements with read/unread state.
 * Users can expand announcements to read the body and mark them as read.
 */

import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@foundation/src/components/ui/card';
import { Badge } from '@foundation/src/components/ui/badge';
import { Button } from '@foundation/src/components/ui/button';
import { Alert, AlertDescription } from '@foundation/src/components/ui/alert';
import {
  Megaphone,
  AlertCircle,
  Loader2,
  ChevronDown,
  ChevronUp,
  CheckCircle2,
  Circle,
  AlertTriangle,
} from 'lucide-react';
import {
  type UserAnnouncement,
  getActiveAnnouncements,
  markAnnouncementRead,
} from '@foundation/src/lib/api/user-announcements-api';
import { formatDistanceToNow } from 'date-fns';

export function MessagesTab() {
  const queryClient = useQueryClient();
  const [expandedId, setExpandedId] = useState<string | null>(null);

  const { data, isLoading, error } = useQuery({
    queryKey: ['announcements'],
    queryFn: async () => {
      const res = await getActiveAnnouncements();
      return res.announcements;
    },
  });

  const announcements = data ?? [];

  const markReadMutation = useMutation({
    mutationFn: markAnnouncementRead,
    onMutate: async (announcementId: string) => {
      // Cancel any outgoing refetches so they don't overwrite our optimistic update
      await queryClient.cancelQueries({ queryKey: ['announcements'] });

      // Snapshot previous value
      const previous = queryClient.getQueryData<UserAnnouncement[]>(['announcements']);

      // Optimistically update
      queryClient.setQueryData<UserAnnouncement[]>(['announcements'], (old) =>
        old?.map((a) => (a.id === announcementId ? { ...a, isRead: true } : a))
      );

      return { previous };
    },
    onError: (_err, _id, context) => {
      // Rollback on error
      if (context?.previous) {
        queryClient.setQueryData(['announcements'], context.previous);
      }
    },
    onSettled: () => {
      // Sync TopBar unread badge
      queryClient.invalidateQueries({ queryKey: ['unread-announcements'] });
    },
  });

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
      <div className="text-center py-12 text-muted-foreground">
        <Loader2 className="h-6 w-6 animate-spin mx-auto mb-2" />
        Loading messages...
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
            <div className="text-center py-8 text-muted-foreground">
              <Megaphone className="h-8 w-8 mx-auto mb-3 opacity-40" />
              <p>No messages at this time.</p>
            </div>
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
                            <Loader2 className="h-4 w-4 animate-spin text-primary" />
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
