-- @migration-class: expand

-- GetActiveForUserAsync joins announcement_reads on both user_id AND announcement_id.
-- The existing idx_announcement_reads_user covers only user_id; extending it with
-- announcement_id creates a covering index that resolves the join in a single lookup.
CREATE INDEX ix_announcement_reads_user_announcement
    ON public.announcement_reads (user_id, announcement_id);
