import { FlaskConical, X } from "lucide-react";

import { Button } from "@foundation/src/components/ui/button";
import { isEphemeralSession } from "@foundation/src/lib/utils/session-end";
import { useSharedDemoStore } from "@foundation/src/store/shared-demo-store";

/** The one-sentence warning, shared with the "Demo" badge popover in the top bar. */
export const SHARED_DEMO_NOTICE =
  "Everyone sees the same data. It resets daily. Do not enter real or personal information.";

/**
 * One slim line above the shell that marks a shared public demo. Styled like the break-glass
 * banner. The visitor can hide it; the "Demo" badge in the top bar brings it back.
 */
export function SharedDemoBanner() {
  const noticeHidden = useSharedDemoStore((s) => s.noticeHidden);
  const setNoticeHidden = useSharedDemoStore((s) => s.setNoticeHidden);

  if (!isEphemeralSession() || noticeHidden) return null;

  return (
    <div
      role="status"
      data-testid="shared-demo-banner"
      className="flex items-center gap-2 border-b border-amber-500/40 bg-amber-500/10 px-4 py-1 text-xs text-amber-900 dark:text-amber-200"
    >
      <FlaskConical className="h-3.5 w-3.5 shrink-0" aria-hidden />
      <span className="font-medium">Shared demo</span>
      <span className="truncate opacity-80 sm:hidden">· resets daily · no personal data</span>
      <span className="hidden opacity-80 sm:inline">· {SHARED_DEMO_NOTICE}</span>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="ml-auto h-6 w-6 shrink-0 text-current hover:bg-amber-500/20 hover:text-current"
        onClick={() => setNoticeHidden(true)}
        aria-label="Hide demo notice"
        data-testid="shared-demo-banner-hide"
      >
        <X className="h-3.5 w-3.5" />
      </Button>
    </div>
  );
}
