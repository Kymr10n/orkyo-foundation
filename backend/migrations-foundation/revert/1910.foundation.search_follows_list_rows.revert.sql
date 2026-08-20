-- Reverts 1910. A renamed list row stops updating the search index, as before.
--
-- Rows already indexed keep the label they were last refreshed with; nothing rewrites them back,
-- and nothing needs to — the index is derived and the next write to the resource refreshes it.

BEGIN;

DROP TRIGGER IF EXISTS trg_search_list_rows_update ON public.list_rows;
DROP FUNCTION IF EXISTS trg_refresh_search_list_row();

COMMIT;
