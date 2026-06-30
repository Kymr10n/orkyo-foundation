-- @migration-class: expand

-- Widen requests_status_check to allow the renamed 'new' token (replacing 'planned')
-- and the new 'deferred' (on-hold) status. Purely additive: 'planned' stays permitted
-- so existing rows and any not-yet-cutover app instance remain valid during rollout.
-- The 'planned' -> 'new' backfill and the DEFAULT flip happen in 1620 (data); dropping
-- 'planned' is deferred to a future contract migration.

ALTER TABLE public.requests
    DROP CONSTRAINT requests_status_check;

ALTER TABLE public.requests
    ADD CONSTRAINT requests_status_check CHECK (
        (status)::text = ANY ((ARRAY[
            'planned'::character varying,   -- legacy; removed in a future contract migration
            'new'::character varying,
            'in_progress'::character varying,
            'done'::character varying,
            'cancelled'::character varying,
            'deferred'::character varying
        ])::text[])
    );
