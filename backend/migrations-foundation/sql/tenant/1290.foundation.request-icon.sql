-- @migration-class: expand
-- Adds an optional display icon to requests. The value is a short string ID
-- (e.g. "calendar", "hammer") resolved to a lucide-react component on the
-- frontend. No backend enum — the curated set lives in the frontend.

ALTER TABLE public.requests
  ADD COLUMN icon varchar(64) NULL;
