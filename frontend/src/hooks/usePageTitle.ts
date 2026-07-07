import { useEffect } from "react";

/** Base suffix appended to every page title, and the document default when no page sets one. */
const BASE_TITLE = "Orkyo";

/**
 * Sets `document.title` to `"<title> — Orkyo"` while the calling page is mounted,
 * restoring the bare base title ("Orkyo") on unmount. Pass the page's own header
 * text so the browser tab mirrors what the user sees on screen.
 */
export function usePageTitle(title: string): void {
  useEffect(() => {
    document.title = `${title} — ${BASE_TITLE}`;
    return () => {
      document.title = BASE_TITLE;
    };
  }, [title]);
}
